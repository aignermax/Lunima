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
    """Run the actual Nazca + GDS extraction, return result dict."""
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
    return result


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
