"""
fdtd_sparams.py - open-source FDTD S-matrix bridge for Lunima.

Reads a JSON spec from stdin describing a component's exported GDS plus its
ports (Lunima knows its pin positions, so it passes them explicitly rather than
having us reconstruct them from the GDS), runs Meep FDTD via gplugins, and
writes an S-matrix JSON to stdout. Intended to run inside the pinned
`lunima-meep` Docker image (Meep is conda-only and has no native Windows build).

Input JSON (stdin):
    {
      "gds_path":    "/work/component.gds",
      "ports": [ {"name":"o1","x":0.0,"y":0.0,"orientation":180,"width":0.5},
                 {"name":"o2","x":12.0,"y":0.0,"orientation":0,"width":0.5} ],
      "layer":       [1, 0],          # silicon layer/datatype (default [1,0])
      "wavelength_start": 1.5,         # um
      "wavelength_stop":  1.6,
      "wavelength_points": 11,
      "resolution":  20,               # pixels/um
      "is_3d":       false,
      "ymargin":     2.0,
      "xmargin":     2.0
    }

Output JSON (stdout) - success:
    { "success": true, "is_3d": false, "resolution": 20,
      "ports": ["o1","o2"], "wavelengths": [...],
      "s": { "o2@0,o1@0": [[re,im], ...], ... },
      "energy_sum_per_input": { "o1@0": 0.996, ... } }

Output JSON (stdout) - failure:
    { "success": false, "error": "...", "missing_backend": "gdsfactory"|null }

Only the MPI master rank emits the JSON, so the output is identical whether run
as `python fdtd_sparams.py` or `mpirun -np N python fdtd_sparams.py`.
"""

import sys
import json


def _is_master():
    try:
        import meep as mp
        return mp.am_master()
    except Exception:
        return True


def _emit(obj):
    if _is_master():
        print(json.dumps(obj))


def _fail(message, missing_backend=None):
    _emit({"success": False, "error": message, "missing_backend": missing_backend})
    sys.exit(0 if missing_backend else 1)


def build_component(spec):
    """
    Build the component geometry and attach the ports supplied by Lunima.

    Geometry comes from explicit polygons when present (e.g. the Nazca preview
    render), otherwise from a GDS file. Ports are always supplied explicitly
    because Lunima knows its own pin positions.
    """
    import gdsfactory as gf
    import gdsfactory.gpdk as gpdk
    gpdk.PDK.activate()

    layer = tuple(spec.get("layer", [1, 0]))
    polygons = spec.get("polygons") or []

    c = gf.Component()
    if polygons:
        for poly in polygons:
            pts = [(float(x), float(y)) for x, y in poly["points"]]
            c.add_polygon(pts, layer=(int(poly.get("layer", layer[0])), 0))
    else:
        c << gf.import_gds(spec["gds_path"])

    for p in spec["ports"]:
        c.add_port(
            p["name"],
            center=(float(p["x"]), float(p["y"])),
            width=float(p["width"]),
            orientation=float(p["orientation"]),
            layer=layer,
        )
    return c


def run(spec):
    try:
        from gplugins.gmeep import write_sparameters_meep
    except ImportError as e:
        missing = "gdsfactory" if "gdsfactory" in str(e) else "gplugins"
        _fail(f"FDTD backend missing ({e}). Install with: pip install gplugins", missing)

    component = build_component(spec)

    sp = write_sparameters_meep(
        component=component,
        is_3d=bool(spec.get("is_3d", False)),
        resolution=int(spec.get("resolution", 20)),
        ymargin=float(spec.get("ymargin", 2.0)),
        xmargin=float(spec.get("xmargin", 2.0)),
        wavelength_start=float(spec.get("wavelength_start", 1.5)),
        wavelength_stop=float(spec.get("wavelength_stop", 1.6)),
        wavelength_points=int(spec.get("wavelength_points", 11)),
        overwrite=True,
    )
    return component, sp


def serialise(component, sp, spec):
    import numpy as np

    keys = [k for k in sp if "@" in k]
    s_out, energy_sum = {}, {}
    for k in keys:
        arr = np.asarray(sp[k])
        s_out[k] = [[float(z.real), float(z.imag)] for z in arr]
        inp = k.split(",")[1]
        energy_sum[inp] = energy_sum.get(inp, 0.0) + float(abs(arr[len(arr) // 2]) ** 2)

    return {
        "success": True,
        "is_3d": bool(spec.get("is_3d", False)),
        "resolution": int(spec.get("resolution", 20)),
        "ports": [p.name for p in component.ports],
        "wavelengths": [float(x) for x in np.asarray(sp["wavelengths"])],
        "s": s_out,
        "energy_sum_per_input": energy_sum,
    }


def _load_spec():
    """Read the JSON spec from a --spec=PATH file (robust under mpirun) or stdin."""
    for a in sys.argv[1:]:
        if a.startswith("--spec="):
            with open(a.split("=", 1)[1]) as f:
                return json.load(f)
    return json.loads(sys.stdin.read())


def main():
    spec = _load_spec()
    component, sp = run(spec)
    _emit(serialise(component, sp, spec))


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as e:
        import traceback
        _emit({"success": False, "error": str(e), "trace": traceback.format_exc()[-1500:]})
        sys.exit(1)
