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
    # Empty __builtins__ blocks attribute-access escapes; expose dict so the
    # eval'd "dict(a=1, b=2)" form actually has a dict to call.
    return eval(f"dict({parameters_string})", {"__builtins__": {}, "dict": dict}, {})


def _build_cell(module_name, function_name, parameters_string):
    """Import module, call function, return nazca cell.

    The module argument can be a dotted attribute path under the demofab
    namespace (e.g. "demo.shallow" → nazca.demofab.shallow). Walk the chain
    so callers don't need to know nazca's internal layout.
    """
    import nazca  # noqa: F401  — initialises Nazca state

    # Defensive: if the function name is dotted (e.g. "demo.mmi2x2_dp" was passed
    # whole), peel the leading module path off so we look up `mmi2x2_dp` against
    # the correct module — not for `demo.mmi2x2_dp` as an attribute name.
    if "." in function_name:
        prefix, function_name = function_name.rsplit(".", 1)
        if module_name in (None, "", "demo") or module_name == prefix:
            module_name = prefix

    # The bundled demo PDK in Nazca ships as `nazca.demofab`. Lunima's PDK
    # JSON refers to it as "demo", "demo_pdk", or with a sub-path like
    # "demo.shallow" / "demo_pdk.shallow.deeper". Resolve all of those.
    if module_name and (module_name == "demo" or module_name.startswith("demo.")
                        or module_name == "demo_pdk" or module_name.startswith("demo_pdk.")):
        import nazca.demofab as mod
        # Walk any sub-path after the leading "demo"/"demo_pdk" segment.
        # "demo.shallow" → mod = mod.shallow
        # "demo_pdk.shallow.deeper" → mod = mod.shallow.deeper
        sub_parts = module_name.split(".", 1)[1:]  # ['shallow'] or []
        for part in (sub_parts[0].split(".") if sub_parts else []):
            mod = getattr(mod, part)
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
            args.module_name, args.function_name, args.stub_length,
            args.parameters_string)
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


def _siepic_libraries(kdb):
    """Yield every EBeam* KLayout library, in registration order."""
    for lid in kdb.Library.library_ids():
        lib = kdb.Library.library_by_id(lid)
        if lib is None:
            continue
        if lib.name().startswith("EBeam"):
            yield lib


def _resolve_siepic_cell(kdb, function_name, parameters_string):
    """
    Locate ``function_name`` across every EBeam* KLayout library and return
    a ``(layout, cell)`` tuple ready for pin/polygon extraction.

    Resolution order, from cheapest to most invasive:

    1. Static cell inside a library layout (e.g. ``GC_TE_1550_8degOxide_BB``
       lives baked-into the EBeam library, not as a PCell). Re-emitted as a
       deep copy into a fresh layout so the caller can read pins/polygons
       without holding a library reference.
    2. PCell with the exact name (verified via ``pcell_names()`` to dodge
       the KLayout API quirk where ``pcell_id("nonexistent")`` returns 0
       instead of -1 — that bug previously made every unknown name resolve
       to PCell #0, which is ``contra_directional_coupler`` and is why
       every "PinCountMismatch x/4" actually rendered the wrong cell).

    Raises FileNotFoundError with a list of likely matches so the caller can
    surface a useful error.
    """
    libs = list(_siepic_libraries(kdb))
    if not libs:
        raise RuntimeError(
            "No EBeam* KLayout libraries are registered. "
            "Importing siepic_ebeam_pdk should register them — check the install.")

    # 1. Static cell in any library layout
    for lib in libs:
        lib_layout = lib.layout()
        for c in lib_layout.each_cell():
            if c.name == function_name:
                return _copy_cell_into_fresh_layout(kdb, lib_layout, c)

    # 2. PCell — but only if the name actually appears in pcell_names()
    user_kwargs = _parse_kwargs(parameters_string)
    for lib in libs:
        lib_layout = lib.layout()
        if function_name not in lib_layout.pcell_names():
            continue
        pcell_id = lib_layout.pcell_id(function_name)
        pcell_decl = lib_layout.pcell_declaration(pcell_id)
        param_values = []
        for p in pcell_decl.get_parameters():
            param_values.append(user_kwargs.get(p.name, p.default))

        ly = kdb.Layout()
        ly.dbu = 0.001  # 1 nm — matches what siepic_ebeam_pdk targets
        var_id = ly.add_pcell_variant(lib, pcell_id, param_values)
        return ly, ly.cell(var_id)

    # Not found anywhere — give the user something actionable.
    suggestions = []
    for lib in libs:
        lib_layout = lib.layout()
        suggestions.extend(c.name for c in lib_layout.each_cell())
        suggestions.extend(lib_layout.pcell_names())
    near = [s for s in suggestions if function_name.lower() in s.lower() or s.lower() in function_name.lower()][:5]
    hint = f" Did you mean: {', '.join(near)}?" if near else ""
    raise FileNotFoundError(
        f"'{function_name}' is neither a fixed-cell GDS nor a static cell nor a "
        f"PCell in any EBeam* library ({', '.join(lib.name() for lib in libs)}).{hint}"
    )


def _copy_cell_into_fresh_layout(kdb, source_layout, source_cell):
    """
    Copy ``source_cell`` (and its hierarchy) into a brand-new ``kdb.Layout``
    so subsequent pin/polygon extraction doesn't mutate the shared library
    layout. Uses KLayout's ``Layout.cell.copy_tree`` semantics via copy().
    """
    ly = kdb.Layout()
    ly.dbu = source_layout.dbu
    new_cell = ly.create_cell(source_cell.name)
    new_cell.copy_tree(source_cell)
    return ly, new_cell


def _looks_like_siepic(module_name):
    """Cheap routing predicate — anything starting with 'siepic' goes through
    the klayout path. The Lunima ViewModel maps every flat ebeam_/gc_ name
    to 'siepic_ebeam_pdk' before we even get the call."""
    return module_name and module_name.lower().startswith("siepic")


def _render_siepic_via_klayout(module_name, function_name, stub_length, parameters_string=""):
    """
    Render a SiEPIC component to polygons + pins. Two paths:

    1. Fixed-cell GDS — siepic_ebeam_pdk/gds/EBeam/<name>.gds. Static
       foundry layout, just read it.
    2. PCell — siepic_ebeam_pdk/pymacros/pcells_EBeam/<name>.py. Goes
       through the EBeam KLayout Library, which siepic_ebeam_pdk
       registers on import.

    Both produce the same JSON shape: polygons from the silicon layer
    (1/0), pins from layer 1/10 (PinRec).
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
    gds_path = os.path.join(pkg_dir, "gds", "EBeam", f"{function_name}.gds")

    if os.path.exists(gds_path):
        ly = kdb.Layout()
        ly.read(gds_path)
        cell = next(ly.each_cell())
    else:
        ly, cell = _resolve_siepic_cell(kdb, function_name, parameters_string)

    # bbox is in database units (typically 1nm); convert to micrometres.
    dbu = ly.dbu
    bb = cell.bbox()
    xmin, ymin = bb.left * dbu, bb.bottom * dbu
    xmax, ymax = bb.right * dbu, bb.top * dbu

    # Pull polygons from every drawing layer that holds geometry. SiEPIC
    # uses 1/0 for silicon on most components but grating couplers etc.
    # live on dedicated layers (e.g. 998/0). Skip the bookkeeping layers:
    #   1/10 = PinRec   68/0 = DevRec   10/0 = FloorPlan / Text labels
    SKIP_LAYERS = {(1, 10), (68, 0), (10, 0)}
    polygons = []
    for li in ly.layer_indexes():
        info = ly.get_info(li)
        if (info.layer, info.datatype) in SKIP_LAYERS:
            continue
        for shape in cell.shapes(li).each():
            try:
                poly = shape.polygon
            except Exception:
                continue
            if poly is None:
                continue
            verts = [[float(p.x * dbu), float(p.y * dbu)] for p in poly.each_point_hull()]
            if len(verts) >= 3:
                polygons.append({"layer": info.layer, "vertices": verts})

    # Pins: SiEPIC stores each pin on layer 1/10 (PinRec) as a Path + a Text.
    # The text label ("opt1", "opt2", …) sits at the pin's xy. The Path
    # encodes the exit direction in its two endpoints — we read the angle
    # from the path nearest to each text label so vertical pins (top/bottom
    # edge) on crossings get reported as 90°/270°, not 0°/180°.
    pin_layer = ly.layer(1, 10)
    paths = []   # (cx, cy, angle_deg)
    texts = []   # (x, y, name)
    for shape in cell.shapes(pin_layer).each():
        if shape.is_text():
            t = shape.text
            texts.append((t.x * dbu, t.y * dbu, t.string))
            continue
        if shape.is_path():
            p = shape.path
            pts = list(p.each_point())
            if len(pts) < 2:
                continue
            # SiEPIC convention: path goes FROM the pin position OUTWARD,
            # so the direction from pts[0] to pts[-1] is the exit direction.
            x0, y0 = pts[0].x * dbu, pts[0].y * dbu
            x1, y1 = pts[-1].x * dbu, pts[-1].y * dbu
            angle = math.degrees(math.atan2(y1 - y0, x1 - x0))
            if angle < 0:
                angle += 360
            paths.append((x0, y0, angle))

    pins = []
    for tx, ty, tname in texts:
        # Pair the text with the closest path endpoint — the path's first
        # vertex is the pin position, which colocates with the text label.
        if paths:
            cx, cy, angle = min(
                paths, key=lambda p: (p[0] - tx) ** 2 + (p[1] - ty) ** 2)
        else:
            # No paths on PinRec — fall back to the bbox-side guess so older
            # cells without explicit paths still get a sensible stub direction.
            cx, cy = tx, ty
            angle = 180.0 if tx < (xmin + xmax) / 2 else 0.0
        rad = math.radians(angle)
        stub_x = tx + stub_length * math.cos(rad)
        stub_y = ty + stub_length * math.sin(rad)
        pins.append({
            "name": tname,
            "x": tx, "y": ty,
            "angle": angle,
            "stubX1": stub_x, "stubY1": stub_y,
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
