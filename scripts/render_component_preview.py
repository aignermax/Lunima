"""
render_component_preview.py — Render a single Nazca component cell and return
bounding-box, polygon and pin data as JSON for the PDK Offset Editor overlay.

Usage:
    python3 render_component_preview.py <module_name> <function_name> [parameters_string] [--stub-length N]

Output (stdout): JSON
    { "success": true,
      "bbox": {"xmin": -5.0, "ymin": -10.0, "xmax": 75.0, "ymax": 45.0},
      "polygons": [{"layer": 1, "vertices": [[x, y], ...]}],
      "pins": [{"name": "a0", "x": 0.0, "y": 27.5, "angle": 180.0,
                "stubX1": -5.0, "stubY1": 27.5}] }

On failure:
    { "success": false, "error": "message" }
"""

import sys
import json
import math
import argparse
import tempfile
import os
import contextlib


def _parse_args():
    parser = argparse.ArgumentParser(description="Render Nazca component preview")
    parser.add_argument("module_name", help="Python module to import (or 'demo')")
    parser.add_argument("function_name", help="Nazca cell function name")
    parser.add_argument("parameters_string", nargs="?", default="",
                        help="Optional keyword arguments as string, e.g. 'length=50'")
    parser.add_argument("--stub-length", type=float, default=3.0,
                        help="Pin stub length in µm (default: 3)")
    return parser.parse_args()


def _parse_kwargs(parameters_string):
    """
    Parse a 'key=value, key=value' string into a kwargs dict.

    PDK JSON files come from foundries / vendors and are already trusted
    (Lunima imports their layouts wholesale anyway), so we use eval with an
    empty builtins namespace — that handles all Python literal forms plus
    things like '5e-6' that would otherwise need extra parsing, while still
    blocking access to dangerous globals.
    """
    if not parameters_string or not parameters_string.strip():
        return {}
    return eval(f"dict({parameters_string})", {"__builtins__": {}}, {})


def _build_cell(module_name, function_name, parameters_string):
    """Import module, call function, return nazca cell."""
    import nazca  # noqa: F401  — initialises Nazca state

    # Defensive: if the function name is dotted (e.g. "demo.mmi2x2_dp" was passed
    # whole), peel the leading module path off so we look up `mmi2x2_dp` against
    # the correct module — not for `demo.mmi2x2_dp` as an attribute name.
    if "." in function_name:
        prefix, function_name = function_name.rsplit(".", 1)
        if module_name in (None, "", "demo") or module_name == prefix:
            module_name = prefix

    # The bundled demo PDK in Nazca ships as `nazca.demofab`. Lunima's PDK
    # JSON refers to it as either "demo" (after our C# split) or sometimes
    # "demo_pdk". Map both to demofab so the user doesn't have to know the
    # internal Nazca naming.
    if module_name in ("demo", "demo_pdk"):
        import nazca.demofab as mod
    else:
        import importlib
        mod = importlib.import_module(module_name)

    func = getattr(mod, function_name)
    kwargs = _parse_kwargs(parameters_string)
    return func(**kwargs) if kwargs else func()


def _extract_bbox(cell):
    """Return (xmin, ymin, xmax, ymax) from a Nazca cell.

    Nazca exposes cell.bbox as a flat 4-tuple (xmin, ymin, xmax, ymax). Older
    revisions used a nested [[xmin, ymin], [xmax, ymax]] form — accept both.
    """
    bb = cell.bbox
    if len(bb) == 4 and not hasattr(bb[0], "__len__"):
        # flat tuple: (xmin, ymin, xmax, ymax)
        return float(bb[0]), float(bb[1]), float(bb[2]), float(bb[3])
    # nested [[xmin, ymin], [xmax, ymax]]
    return float(bb[0][0]), float(bb[0][1]), float(bb[1][0]), float(bb[1][1])


def _extract_pins(cell, stub_length):
    """Return list of pin dicts with stub endpoints.

    Nazca puts a fixed set of bookkeeping pins on every cell — origin, the
    nine bbox-corner/edge anchors (lb, lc, lt, tl, tc, tr, rt, rc, rb, br,
    bc, bl) and the center 'cc'. These are not optical ports and would
    clutter the offset editor with phantom pin dots. Filter them out.
    """
    INTERNAL = {"org", "cc",
                "lb", "lc", "lt",
                "tl", "tc", "tr",
                "rt", "rc", "rb",
                "br", "bc", "bl"}
    pins = []
    for name, pin in cell.pin.items():
        if name in INTERNAL:
            continue
        x, y, angle = pin.xya()
        rad = math.radians(angle)
        stub_x1 = x + stub_length * math.cos(rad)
        stub_y1 = y + stub_length * math.sin(rad)
        pins.append({
            "name": name,
            "x": float(x),
            "y": float(y),
            "angle": float(angle),
            "stubX1": float(stub_x1),
            "stubY1": float(stub_y1),
        })
    return pins


def _extract_polygons(gds_path):
    """
    Extract polygons from a GDS file. Prefers gdstk (modern, faster,
    actively maintained) and falls back to gdspy. Raises ImportError when
    neither is installed so the caller can surface a friendly message.
    """
    try:
        import gdstk
        return _extract_polygons_gdstk(gds_path)
    except ImportError:
        pass

    try:
        import gdspy  # noqa: F401
        return _extract_polygons_gdspy(gds_path)
    except ImportError as exc:
        raise ImportError(
            "Neither gdstk nor gdspy is installed — cannot read GDS polygons.") from exc


def _extract_polygons_gdstk(gds_path):
    import gdstk
    lib = gdstk.read_gds(gds_path)
    polygons = []
    for cell in lib.cells:
        for poly in cell.polygons:
            polygons.append({
                "layer": int(poly.layer),
                "vertices": [[float(v[0]), float(v[1])] for v in poly.points],
            })
    return polygons


def _extract_polygons_gdspy(gds_path):
    import gdspy
    lib = gdspy.GdsLibrary(infile=gds_path)
    polygons = []
    for cell in lib.cells.values():
        for poly in cell.polygons:
            for i, verts in enumerate(poly.polygons):
                layer = poly.layers[i] if i < len(poly.layers) else 0
                polygons.append({
                    "layer": int(layer),
                    "vertices": [[float(v[0]), float(v[1])] for v in verts],
                })
    return polygons


def _render_to_gds(cell):
    """Export cell to a temp GDS file, return path."""
    import nazca
    tmp = tempfile.mktemp(suffix=".gds")
    nazca.export_gds(topcells=[cell], filename=tmp)
    return tmp


def _do_render(args):
    """
    Run the actual rendering, return result dict.

    For SiEPIC EBeam PDK components, route through klayout — siepic_ebeam_pdk
    ships fixed-cell GDS files with the real foundry geometry. For demofab
    and other Nazca-renderable PDKs, build the cell via Nazca and export.
    """
    if _looks_like_siepic(args.module_name):
        result = _render_siepic_via_klayout(
            args.module_name, args.function_name, args.stub_length)
        result["source"] = _fetch_siepic_source(args.module_name, args.function_name)
        return result

    cell = _build_cell(args.module_name, args.function_name, args.parameters_string)
    xmin, ymin, xmax, ymax = _extract_bbox(cell)
    pins = _extract_pins(cell, args.stub_length)

    polygons = []
    polygon_warning = None
    gds_path = None
    try:
        gds_path = _render_to_gds(cell)
        polygons = _extract_polygons(gds_path)
    except ImportError:
        polygon_warning = (
            "Polygon overlay requires gdstk or gdspy — install one of them: "
            "`pip install gdstk` (faster, recommended) or `pip install gdspy`. "
            "Showing pin stubs only for now.")
    except Exception as poly_err:
        polygon_warning = f"polygon extraction failed: {poly_err}"
    finally:
        if gds_path and os.path.exists(gds_path):
            os.remove(gds_path)

    result = {
        "success": True,
        "bbox": {
            "xmin": float(xmin),
            "ymin": float(ymin),
            "xmax": float(xmax),
            "ymax": float(ymax),
        },
        "polygons": polygons,
        "pins": pins,
    }
    if polygon_warning:
        result["polygon_warning"] = polygon_warning
    result["source"] = _fetch_nazca_source(args.module_name, args.function_name)
    return result


def _fetch_nazca_source(module_name, function_name):
    """
    Pull the actual Python source of a Nazca-renderable cell function via
    inspect. For demofab, this surfaces the real `nazca.demofab.<name>`
    body — what the package actually computes when we call it. Returns a
    descriptive note when the source can't be retrieved (e.g. C-extension).
    """
    try:
        import inspect
        if module_name == "demo":
            import nazca.demofab as mod
        else:
            import importlib
            mod = importlib.import_module(module_name)
        target = function_name.rsplit(".", 1)[-1]
        func = getattr(mod, target, None)
        if func is None:
            return f"# {module_name}.{target}: attribute not found"
        try:
            return inspect.getsource(func)
        except (TypeError, OSError) as exc:
            return f"# Could not read source for {module_name}.{target}: {exc}"
    except Exception as exc:
        return f"# Source unavailable: {exc}"


def _fetch_siepic_source(module_name, function_name):
    """
    For SiEPIC, components live in two places: fixed cells under gds/EBeam/
    (no Python — return GDS path + size), or PCells under
    pymacros/pcells_EBeam/<name>.py (read the file directly).
    """
    try:
        import importlib
        mod = importlib.import_module(module_name)
        pkg_dir = os.path.dirname(mod.__file__)
        # PCell: real Python source under pymacros/pcells_EBeam/<name>.py
        pcell_path = os.path.join(pkg_dir, "pymacros", "pcells_EBeam", f"{function_name}.py")
        if os.path.exists(pcell_path):
            with open(pcell_path, "r", encoding="utf-8") as f:
                return f.read()
        # Fixed-cell GDS: no Python; describe the file Lunima will read
        gds_path = os.path.join(pkg_dir, "gds", "EBeam", f"{function_name}.gds")
        if os.path.exists(gds_path):
            size = os.path.getsize(gds_path)
            return (
                f"# {function_name} is a fixed-cell GDS in the SiEPIC package — no Python source.\n"
                f"# Lunima loads the foundry layout directly from:\n"
                f"#   {gds_path}\n"
                f"# Size: {size} bytes\n")
        return f"# Source unavailable: no PCell or fixed-cell GDS found for '{function_name}' in {module_name}"
    except Exception as exc:
        return f"# Source unavailable: {exc}"


def _looks_like_siepic(module_name):
    """Cheap routing predicate — anything starting with 'siepic' goes through
    the klayout path. The Lunima ViewModel maps every flat ebeam_/gc_ name
    to 'siepic_ebeam_pdk' before we even get the call."""
    return module_name and module_name.lower().startswith("siepic")


def _render_siepic_via_klayout(module_name, function_name, stub_length):
    """
    Read SiEPIC's bundled fixed-cell GDS (siepic_ebeam_pdk/gds/EBeam/<name>.gds)
    via klayout python and return the same JSON shape as the Nazca path —
    polygons from the silicon layer, pins from layer 1/10 (PinRec).
    """
    try:
        import klayout.db as kdb
    except ImportError as exc:
        raise ImportError(
            "Rendering SiEPIC components requires klayout-python: "
            "`pip install klayout`."
        ) from exc

    try:
        import siepic_ebeam_pdk
    except ImportError as exc:
        raise ImportError(
            "siepic_ebeam_pdk is not installed in this Python environment. "
            "Install via `pip install siepic_ebeam_pdk`."
        ) from exc

    pkg_dir = os.path.dirname(siepic_ebeam_pdk.__file__)
    # SiEPIC organises fixed-cell GDS files under gds/EBeam/<function>.gds.
    # Only the EBeam library is in scope for now — the SiN, Beta, ANT,
    # Dream variants follow the same pattern and could be added by name.
    gds_path = os.path.join(pkg_dir, "gds", "EBeam", f"{function_name}.gds")
    if not os.path.exists(gds_path):
        raise FileNotFoundError(
            f"No fixed-cell GDS for '{function_name}' under {os.path.dirname(gds_path)}. "
            "Parametric SiEPIC components (PCells) are not yet supported in the preview.")

    ly = kdb.Layout()
    ly.read(gds_path)
    cell = next(ly.each_cell())

    # bbox is in database units (typically 1nm); convert to micrometres.
    dbu = ly.dbu
    bb = cell.bbox()
    xmin, ymin = bb.left * dbu, bb.bottom * dbu
    xmax, ymax = bb.right * dbu, bb.top * dbu

    # Pull every polygon on the silicon waveguide layer (1/0). Other layers
    # are device outline / floorplan and would clutter the overlay.
    si_layer = ly.layer(1, 0)
    polygons = []
    for shape in cell.shapes(si_layer).each():
        # shape.polygon converts boxes / paths to a polygon for free.
        try:
            poly = shape.polygon
        except Exception:
            continue
        if poly is None:
            continue
        verts = [[float(p.x * dbu), float(p.y * dbu)] for p in poly.each_point_hull()]
        if len(verts) >= 3:
            polygons.append({"layer": 1, "vertices": verts})

    # Pins: SiEPIC stores them on layer 1/10 (PinRec) as a Path + a Text.
    # The text label ("opt1", "opt2", …) sits at the pin's xy.
    pin_layer = ly.layer(1, 10)
    pins = []
    for shape in cell.shapes(pin_layer).each():
        if not shape.is_text():
            continue
        t = shape.text
        x = t.x * dbu
        y = t.y * dbu
        # We don't have the exit angle directly from the text; SiEPIC paths
        # carry it, but for the overlay a pin dot at (x, y) is enough.
        # Stub endpoint: extend horizontally by stub_length toward the bbox
        # edge nearest to the pin.
        sign = -1.0 if x < (xmin + xmax) / 2 else 1.0
        stub_x = x + sign * stub_length
        pins.append({
            "name": t.string,
            "x": x, "y": y,
            "angle": 180.0 if sign < 0 else 0.0,
            "stubX1": stub_x, "stubY1": y,
        })

    return {
        "success": True,
        "bbox": {"xmin": xmin, "ymin": ymin, "xmax": xmax, "ymax": ymax},
        "polygons": polygons,
        "pins": pins,
    }


def main():
    args = _parse_args()

    # Nazca prints various chatter on stdout during import and rendering
    # ("loaded ...", "layer ...", etc.). Redirect that to stderr so it doesn't
    # corrupt the JSON our caller (NazcaComponentPreviewService) expects on
    # stdout. The caller already reads stderr separately for diagnostics.
    result = None
    with contextlib.redirect_stdout(sys.stderr):
        try:
            result = _do_render(args)
        except Exception as exc:
            result = {"success": False, "error": str(exc)}

    # Outside the redirect — write the JSON to the real stdout.
    print(json.dumps(result))
    sys.exit(0)  # non-exception exit so the C# parser reads our stdout


if __name__ == "__main__":
    main()
