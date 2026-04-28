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
    Parse a 'key=value, key=value' string into a kwargs dict using
    ast.literal_eval per value — never eval/exec on PDK-supplied input.

    Values must be Python literals (numbers, strings, lists, dicts, tuples,
    booleans, None). Raises ValueError on malformed input.
    """
    import ast
    if not parameters_string or not parameters_string.strip():
        return {}

    result = {}
    for pair in parameters_string.split(","):
        pair = pair.strip()
        if not pair:
            continue
        if "=" not in pair:
            raise ValueError(f"Invalid parameter (missing '='): {pair!r}")
        key, raw_value = pair.split("=", 1)
        key = key.strip()
        if not key.isidentifier():
            raise ValueError(f"Invalid parameter key {key!r} (must be a Python identifier)")
        try:
            result[key] = ast.literal_eval(raw_value.strip())
        except (ValueError, SyntaxError) as exc:
            raise ValueError(f"Cannot parse value for {key!r}: {exc}") from exc
    return result


def _build_cell(module_name, function_name, parameters_string):
    """Import module, call function, return nazca cell."""
    import nazca  # noqa: F401  — initialises Nazca state

    if module_name == "demo":
        # The bundled demo PDK in nazca ships as `demofab` — same name used
        # everywhere else in this codebase (NazcaHeader.txt, reference scripts).
        import nazca.demofab as mod
    else:
        import importlib
        mod = importlib.import_module(module_name)

    func = getattr(mod, function_name)
    kwargs = _parse_kwargs(parameters_string)
    return func(**kwargs) if kwargs else func()


def _extract_bbox(cell):
    """Return (xmin, ymin, xmax, ymax) from a Nazca cell."""
    bb = cell.bbox
    # bb is [[xmin, ymin], [xmax, ymax]] in Nazca
    return bb[0][0], bb[0][1], bb[1][0], bb[1][1]


def _extract_pins(cell, stub_length):
    """Return list of pin dicts with stub endpoints."""
    pins = []
    for name, pin in cell.pin.items():
        if name in ("org",):
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


def _extract_polygons_gdspy(gds_path):
    """Extract polygons from GDS file using gdspy."""
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


def main():
    args = _parse_args()

    try:
        cell = _build_cell(args.module_name, args.function_name, args.parameters_string)
        xmin, ymin, xmax, ymax = _extract_bbox(cell)
        pins = _extract_pins(cell, args.stub_length)

        polygons = []
        gds_path = None
        try:
            gds_path = _render_to_gds(cell)
            polygons = _extract_polygons_gdspy(gds_path)
        except ImportError:
            # gdspy not installed — return bbox + pins only
            pass
        except Exception as poly_err:
            # Polygon extraction failed gracefully; continue with bbox + pins
            sys.stderr.write(f"polygon extraction warning: {poly_err}\n")
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
        print(json.dumps(result))

    except Exception as exc:
        error_result = {"success": False, "error": str(exc)}
        print(json.dumps(error_result))
        sys.exit(0)  # non-exception exit so C# reads stdout


if __name__ == "__main__":
    main()
