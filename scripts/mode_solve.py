"""
mode_solve.py — Mode-solver bridge for Lunima.

Reads a JSON waveguide cross-section spec from stdin, runs the requested
Python backend, and writes a JSON result to stdout.

Input JSON (stdin):
    {
      "width":       0.45,            # core width, µm
      "height":      0.22,            # core height / thickness, µm
      "slab_height": 0.0,             # slab height (0 = fully-etched strip)
      "core_index":  3.48,            # real part of core refractive index
      "clad_index":  1.44,            # real part of cladding index
      "wavelengths": [1.55],          # wavelengths in µm
      "backend":     "GdsfactoryModes",  # "GdsfactoryModes" | "EMpy" | "Tidy3D"
      "num_modes":   4                # max modes to return
    }

Output JSON (stdout) — success:
    {
      "success":      true,
      "backend_used": "GdsfactoryModes",
      "modes": [
        {
          "wavelength":     1.55,
          "mode_index":     0,
          "n_eff":          2.45,
          "n_g":            4.2,
          "polarisation":   "TE",
          "mode_field_png": "<base64-encoded PNG | null>"
        }
      ]
    }

Output JSON (stdout) — failure:
    {
      "success":        false,
      "error":          "human-readable message",
      "missing_backend": "gdsfactory"  # optional: which pip package to install
    }

Nazca / femwell chatter printed on stdout during import is silenced by
redirecting stdout to stderr before we read from stdin — the final JSON is
the only thing that lands on real stdout.
"""

import sys
import json
import base64
import math
import contextlib
import io


# ---------------------------------------------------------------------------
# I/O helpers
# ---------------------------------------------------------------------------

def _load_request():
    """Read the full stdin and parse as JSON."""
    return json.loads(sys.stdin.read())


# ---------------------------------------------------------------------------
# Backend: gdsfactory.simulation.modes / femwell
# ---------------------------------------------------------------------------

_MISSING_PKG_SENTINEL = "__missing_pkg__:"


def _missing_pkg(pkg: str) -> str:
    """Return a sentinel string that _dispatch can recognise as a missing-package hint."""
    return f"{_MISSING_PKG_SENTINEL}{pkg}"


def _solve_gdsfactory(req: dict) -> tuple:
    """
    Attempt to solve using gdsfactory + femwell.
    Returns (modes_list, None) on success or (None, sentinel) on ImportError
    so the caller can surface a helpful install hint.
    """
    try:
        import gdsfactory as gf  # noqa: F401
    except ImportError:
        return None, _missing_pkg("gdsfactory")

    # gdsfactory ≥ 7 ships mode solving via femwell under
    # gdsfactory.simulation.fem.mode_solver.  Earlier versions used
    # gdsfactory.simulation.modes.  Try both.
    solver_fn = _try_import_gdsfactory_solver()
    if solver_fn is None:
        return None, _missing_pkg("gdsfactory[femwell]")

    width       = req["width"]
    height      = req["height"]
    slab_height = req.get("slab_height", 0.0)
    core_index  = req.get("core_index",  3.48)
    clad_index  = req.get("clad_index",  1.44)
    wavelengths = req["wavelengths"]
    num_modes   = int(req.get("num_modes", 4))

    modes = []
    for wl in wavelengths:
        raw = solver_fn(
            wavelength       = float(wl),
            core_width       = float(width),
            core_thickness   = float(height),
            slab_thickness   = float(slab_height),
            core_material    = float(core_index),
            clad_material    = float(clad_index),
            num_modes        = num_modes,
        )
        for i, m in enumerate(raw[:num_modes]):
            n_eff         = float(getattr(m, "n_eff", 0))
            n_g           = float(getattr(m, "n_g",   n_eff))
            polarisation  = _classify_polarisation_gf(m)
            mode_field_b64 = _mode_field_to_b64(m)
            modes.append({
                "wavelength":     float(wl),
                "mode_index":     i,
                "n_eff":          n_eff,
                "n_g":            n_g,
                "polarisation":   polarisation,
                "mode_field_png": mode_field_b64,
            })

    return modes, None


def _try_import_gdsfactory_solver():
    """Return the mode-solver callable, or None if unavailable."""
    # Modern gdsfactory (≥ 7) — femwell backend
    try:
        from gdsfactory.simulation.fem.mode_solver import compute_cross_section_modes
        return compute_cross_section_modes
    except ImportError:
        pass

    # Older gdsfactory — legacy modes module
    try:
        from gdsfactory.simulation.modes import find_modes_waveguide
        return find_modes_waveguide
    except ImportError:
        pass

    return None


def _classify_polarisation_gf(mode) -> str:
    """Classify a gdsfactory mode as TE, TM, or hybrid from te_fraction."""
    te = float(getattr(mode, "te_fraction", 0.5))
    if te > 0.7:
        return "TE"
    if te < 0.3:
        return "TM"
    return "hybrid"


def _mode_field_to_b64(mode) -> str | None:
    """Render the mode field to a base64 PNG string, or return None."""
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt

        fig, ax = plt.subplots(figsize=(3, 3))
        if hasattr(mode, "plot"):
            mode.plot(ax=ax)
        elif hasattr(mode, "Ex"):
            ax.imshow(abs(mode.Ex) ** 2, cmap="inferno", origin="lower")
            ax.set_title(f"|Ex|² mode {getattr(mode, 'mode_index', '')}")
        else:
            plt.close(fig)
            return None

        buf = io.BytesIO()
        fig.savefig(buf, format="png", bbox_inches="tight", dpi=80)
        plt.close(fig)
        buf.seek(0)
        return base64.b64encode(buf.read()).decode("ascii")
    except Exception:
        return None


# ---------------------------------------------------------------------------
# Backend: EMpy
# ---------------------------------------------------------------------------

def _solve_empy(req: dict) -> tuple:
    """
    Solve using EMpy — a lightweight pure-Python FDE solver.
    Returns (modes_list, None) on success or (None, missing_pkg) on ImportError.
    """
    try:
        import EMpy  # noqa: F401
    except ImportError:
        return None, _missing_pkg("EMpy")

    try:
        import numpy as np
        from EMpy.modesolvers.FD import VFDModeSolver
    except ImportError:
        return None, _missing_pkg("EMpy")

    width       = float(req["width"])
    height      = float(req["height"])
    core_index  = float(req.get("core_index", 3.48))
    clad_index  = float(req.get("clad_index", 1.44))
    wavelengths = req["wavelengths"]
    num_modes   = int(req.get("num_modes", 4))

    # Simulation window (pad by 1 µm each side)
    wnd_x = width  + 2.0
    wnd_y = height + 2.0
    nx, ny = 64, 64

    x = np.linspace(-wnd_x / 2, wnd_x / 2, nx)
    y = np.linspace(-wnd_y / 2, wnd_y / 2, ny)

    # Build refractive-index profile
    def eps_func(x_val, y_val):
        in_core = abs(x_val) <= width / 2 and abs(y_val) <= height / 2
        n = core_index if in_core else clad_index
        return n ** 2

    modes = []
    for wl in wavelengths:
        try:
            solver = VFDModeSolver(float(wl), x, y, eps_func, boundary="0000")
            solver.solve(num_modes, tol=1e-8, mode_kw={})
            for i, mode in enumerate(solver.modes[:num_modes]):
                n_eff = float(mode.neff.real)
                n_g   = float(getattr(mode, "ng", n_eff))
                polarisation = _classify_polarisation_empy(mode)
                modes.append({
                    "wavelength":     float(wl),
                    "mode_index":     i,
                    "n_eff":          n_eff,
                    "n_g":            n_g,
                    "polarisation":   polarisation,
                    "mode_field_png": None,
                })
        except Exception as exc:
            return None, f"EMpy solve error at λ={wl}: {exc}"

    return modes, None


def _classify_polarisation_empy(mode) -> str:
    """Classify EMpy mode polarisation from Ex/Ey field amplitudes."""
    try:
        import numpy as np
        ex_power = float(np.sum(np.abs(mode.Ex) ** 2))
        ey_power = float(np.sum(np.abs(mode.Ey) ** 2))
        total = ex_power + ey_power
        if total == 0:
            return "hybrid"
        te_frac = ex_power / total
        if te_frac > 0.7:
            return "TE"
        if te_frac < 0.3:
            return "TM"
        return "hybrid"
    except Exception:
        return "hybrid"


# ---------------------------------------------------------------------------
# Backend: Tidy3D
# ---------------------------------------------------------------------------

def _solve_tidy3d(req: dict) -> tuple:
    """
    Solve using Tidy3D mode-solver (local FDE, no cloud required for mode
    decomposition).  Returns (modes_list, None) on success or (None, pkg) on error.
    """
    try:
        import tidy3d as td
        import tidy3d.plugins.mode as mode_plugin
    except ImportError:
        return None, _missing_pkg("tidy3d")

    try:
        import numpy as np

        width       = float(req["width"])
        height      = float(req["height"])
        core_index  = float(req.get("core_index", 3.48))
        clad_index  = float(req.get("clad_index", 1.44))
        wavelengths = req["wavelengths"]
        num_modes   = int(req.get("num_modes", 4))

        # Cross-section geometry
        core  = td.Structure(
            geometry=td.Box(center=(0, 0, 0), size=(width, height, 0)),
            medium=td.Medium(permittivity=core_index ** 2))
        clad  = td.Structure(
            geometry=td.Box(center=(0, 0, 0), size=(td.inf, td.inf, 0)),
            medium=td.Medium(permittivity=clad_index ** 2))

        modes = []
        for wl in wavelengths:
            freq  = td.C_0 / float(wl)
            mode_spec = td.ModeSpec(num_modes=num_modes)
            plane = td.Box(center=(0, 0, 0), size=(width * 4, height * 4, 0))
            solver = mode_plugin.ModeSolver(
                simulation=td.Simulation(
                    size=plane.size,
                    structures=[clad, core],
                    medium=td.Medium(permittivity=clad_index ** 2),
                    grid_spec=td.GridSpec.auto(wavelength=float(wl)),
                    run_time=1e-12,
                    boundary_spec=td.BoundarySpec.all_sides(td.PML()),
                ),
                plane=plane,
                mode_spec=mode_spec,
                freqs=[freq],
            )
            data = solver.solve()
            for i in range(num_modes):
                n_eff = float(data.n_eff.sel(mode_index=i, f=freq).values.real)
                n_g   = float(data.n_group.sel(mode_index=i, f=freq).values.real
                              if hasattr(data, "n_group") else n_eff)
                modes.append({
                    "wavelength":     float(wl),
                    "mode_index":     i,
                    "n_eff":          n_eff,
                    "n_g":            n_g,
                    # TODO: classify from data.Ex / data.Ey field magnitudes
                    # instead of assuming TE for every mode.
                    "polarisation":   "TE",   # Tidy3D enumerates TE-first
                    "mode_field_png": None,
                })

        return modes, None

    except Exception as exc:
        return None, f"tidy3d solve error: {exc}"


# ---------------------------------------------------------------------------
# Backend dispatch
# ---------------------------------------------------------------------------

_BACKEND_SOLVERS = {
    "GdsfactoryModes": (_solve_gdsfactory, "gdsfactory"),
    "EMpy":            (_solve_empy,        "EMpy"),
    "Tidy3D":          (_solve_tidy3d,      "tidy3d"),
}


def _dispatch(req: dict) -> dict:
    """Select and run the requested backend, return result dict."""
    backend_name = req.get("backend", "GdsfactoryModes")

    if backend_name not in _BACKEND_SOLVERS:
        known = ", ".join(_BACKEND_SOLVERS)
        return {
            "success": False,
            "error":   f"Unknown backend '{backend_name}'. Known backends: {known}",
        }

    solver_fn, pkg_hint = _BACKEND_SOLVERS[backend_name]
    modes, error = solver_fn(req)

    if modes is None:
        if error and error.startswith(_MISSING_PKG_SENTINEL):
            pkg = error[len(_MISSING_PKG_SENTINEL):]
            return {
                "success":        False,
                "error":          (
                    f"Backend '{backend_name}' requires the '{pkg}' Python package. "
                    f"Install it with: pip install {pkg}"
                ),
                "missing_backend": pkg,
            }
        return {"success": False, "error": str(error)}

    return {
        "success":      True,
        "backend_used": backend_name,
        "modes":        modes,
    }


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    result = None
    # Redirect stdout → stderr while we work so that library import chatter
    # (femwell, gdsfactory, etc.) doesn't corrupt the JSON we return.
    with contextlib.redirect_stdout(sys.stderr):
        try:
            req    = _load_request()
            result = _dispatch(req)
        except json.JSONDecodeError as exc:
            result = {"success": False, "error": f"Invalid JSON input: {exc}"}
        except Exception as exc:
            result = {"success": False, "error": str(exc)}

    # Write the JSON result to the real stdout.
    print(json.dumps(result), flush=True)
    sys.exit(0)


if __name__ == "__main__":
    main()
