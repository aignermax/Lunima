"""
PDK Parser: Extract component geometry from a Nazca PDK module.
Issue #460: PDK Import Tool — Python/Nazca PDK parser with visual verification.

Imports a Nazca PDK module and extracts for each cell:
  - Bounding box (width, height in µm)
  - Pin names, positions, and angles (converted to editor coordinate space)
  - nazcaOriginOffset — where the Nazca cell (0,0) sits in editor coordinates

The output JSON matches the Connect-A-PIC PDK format and can be used directly
as a PDK file after adding S-matrix data.

Usage:
    python3 scripts/parse_pdk.py <module_name> [function1 function2 ...]
    python3 scripts/parse_pdk.py siepic_ebeam_pdk > siepic-ebeam-pdk-geometry.json
    python3 scripts/parse_pdk.py siepic_ebeam_pdk ebeam_y_1550 ebeam_dc_te1550

Coordinate conventions:
    Nazca space: X right, Y up
    Editor space: X right, Y down
    nazcaOriginOffsetX = -xmin  (distance of Nazca origin from left bbox edge)
    nazcaOriginOffsetY = ymax   (distance of Nazca origin from top bbox edge)
    pinEditorX = pinNazcaX - xmin
    pinEditorY = ymax - pinNazcaY
"""

from __future__ import annotations

import json
import sys
import importlib
from typing import Any


# ---------------------------------------------------------------------------
# Geometry helpers
# ---------------------------------------------------------------------------

def compute_origin_offset(xmin: float, ymax: float) -> tuple[float, float]:
    """
    Compute the nazcaOriginOffset from the bounding-box extremes.

    In editor coordinates (Y-down), the Nazca cell origin (0, 0) maps to:
      editor_x = 0 - xmin
      editor_y = ymax - 0
    """
    return -xmin, ymax


def nazca_to_editor(
    nazca_x: float,
    nazca_y: float,
    xmin: float,
    ymax: float,
) -> tuple[float, float]:
    """Convert a Nazca-space point to editor space (Y-down, origin at top-left of bbox)."""
    editor_x = nazca_x - xmin
    editor_y = ymax - nazca_y
    return editor_x, editor_y


def normalize_angle(nazca_angle: float) -> float:
    """
    Convert a Nazca pin angle to an editor angle.

    Nazca uses:  0° = pointing right, 180° = pointing left (standard convention)
    Editor uses: same convention (connections come in from the outside, so
                 a pin facing left at 180° means the waveguide enters from the left).

    Y-flip does NOT invert horizontal angles (0° / 180°), but does invert
    vertical ones: Nazca 90° (up) → editor 270° (down).
    """
    # Map common Nazca angles to editor angles after Y-axis flip
    angle_mod = nazca_angle % 360
    flipped = (-angle_mod) % 360
    return flipped


# ---------------------------------------------------------------------------
# Cell analysis
# ---------------------------------------------------------------------------

def analyze_cell(func_name: str, cell_factory) -> dict[str, Any] | None:
    """
    Instantiate a Nazca cell via *cell_factory* and extract geometry.

    Returns a dict compatible with the PdkComponentDraft JSON format,
    or None if the cell cannot be instantiated.
    """
    try:
        import nazca  # type: ignore

        with nazca.Cell("_parse_probe") as probe:
            inst = cell_factory()
    except Exception as exc:
        print(f"  [WARN] Could not instantiate {func_name}: {exc}", file=sys.stderr)
        return None

    bbox = inst.bbox
    if not bbox:
        print(f"  [WARN] No bounding box for {func_name}", file=sys.stderr)
        return None

    xmin, ymin, xmax, ymax = bbox
    width = xmax - xmin
    height = ymax - ymin

    if width <= 0 or height <= 0:
        print(f"  [WARN] Degenerate bbox for {func_name}: {bbox}", file=sys.stderr)
        return None

    origin_offset_x, origin_offset_y = compute_origin_offset(xmin, ymax)

    # Extract pins
    pins = []
    for pin_name, pin in inst.pin.items():
        ex, ey = nazca_to_editor(pin.x, pin.y, xmin, ymax)
        ea = normalize_angle(pin.a)
        pins.append({
            "name": pin_name,
            "offsetXMicrometers": round(ex, 4),
            "offsetYMicrometers": round(ey, 4),
            "angleDegrees": round(ea, 1),
        })

    return {
        "name": func_name,
        "category": "Imported",
        "nazcaFunction": func_name,
        "nazcaParameters": "",
        "widthMicrometers": round(width, 4),
        "heightMicrometers": round(height, 4),
        "nazcaOriginOffsetX": round(origin_offset_x, 4),
        "nazcaOriginOffsetY": round(origin_offset_y, 4),
        "pins": pins,
        "sMatrix": None,
    }


# ---------------------------------------------------------------------------
# PDK module inspection
# ---------------------------------------------------------------------------

def list_pdk_functions(module) -> list[str]:
    """
    Return public callable names from a PDK module that look like cell factories.
    Filters out private names (underscore prefix) and known non-cell names.
    """
    skip = {"Cell", "put", "strt", "bend", "taper", "interconnect"}
    return [
        name for name in dir(module)
        if not name.startswith("_")
        and callable(getattr(module, name))
        and name not in skip
    ]


def parse_pdk(module_name: str, function_names: list[str] | None = None) -> dict[str, Any]:
    """
    Import *module_name* and parse the requested (or all) cell functions.

    Returns a dict in Connect-A-PIC PdkDraft JSON format.
    """
    try:
        pdk_module = importlib.import_module(module_name)
    except ImportError as exc:
        print(f"ERROR: Cannot import '{module_name}': {exc}", file=sys.stderr)
        print("Make sure the PDK module is installed in the current Python environment.",
              file=sys.stderr)
        sys.exit(1)

    if function_names:
        funcs_to_parse = function_names
    else:
        funcs_to_parse = list_pdk_functions(pdk_module)
        print(f"Found {len(funcs_to_parse)} public callables in '{module_name}'.",
              file=sys.stderr)

    components = []
    for func_name in funcs_to_parse:
        factory = getattr(pdk_module, func_name, None)
        if factory is None:
            print(f"  [WARN] Function '{func_name}' not found in module.", file=sys.stderr)
            continue
        if not callable(factory):
            print(f"  [SKIP] '{func_name}' is not callable.", file=sys.stderr)
            continue

        print(f"  Parsing {func_name}…", file=sys.stderr)
        comp = analyze_cell(func_name, lambda f=factory: f())
        if comp is not None:
            components.append(comp)

    # Use module __version__ if available
    version = getattr(pdk_module, "__version__", "unknown")

    return {
        "fileFormatVersion": 1,
        "name": module_name,
        "description": f"Auto-generated from Python module '{module_name}'",
        "foundry": "unknown — fill in manually",
        "version": str(version),
        "defaultWavelengthNm": 1550,
        "nazcaModuleName": module_name,
        "components": components,
    }


# ---------------------------------------------------------------------------
# Demo fallback (no Nazca required)
# ---------------------------------------------------------------------------

def demo_output() -> dict[str, Any]:
    """
    Return a sample output for demofab components (no Nazca import required).
    Hard-coded reference values used when Nazca is not installed.
    """
    return {
        "fileFormatVersion": 1,
        "name": "demo (reference values)",
        "description": "Reference geometry for Nazca demofab — install nazca for live parsing",
        "foundry": "Nazca demofab",
        "version": "reference",
        "defaultWavelengthNm": 1550,
        "nazcaModuleName": "nazca.demofab",
        "components": [
            {
                "name": "mmi1x2_sh",
                "category": "Splitters",
                "nazcaFunction": "mmi1x2_sh",
                "nazcaParameters": "",
                "widthMicrometers": 80,
                "heightMicrometers": 55,
                "nazcaOriginOffsetX": 0.0,
                "nazcaOriginOffsetY": 27.5,
                "pins": [
                    {"name": "a0", "offsetXMicrometers": 0.0, "offsetYMicrometers": 27.5, "angleDegrees": 180.0},
                    {"name": "b0", "offsetXMicrometers": 80.0, "offsetYMicrometers": 25.5, "angleDegrees": 0.0},
                    {"name": "b1", "offsetXMicrometers": 80.0, "offsetYMicrometers": 29.5, "angleDegrees": 0.0},
                ],
                "sMatrix": None,
            },
            {
                "name": "mmi2x2_dp",
                "category": "Couplers",
                "nazcaFunction": "mmi2x2_dp",
                "nazcaParameters": "",
                "widthMicrometers": 250,
                "heightMicrometers": 60,
                "nazcaOriginOffsetX": 0.0,
                "nazcaOriginOffsetY": 26.0,
                "pins": [
                    {"name": "a0", "offsetXMicrometers": 0.0, "offsetYMicrometers": 22.0, "angleDegrees": 180.0},
                    {"name": "a1", "offsetXMicrometers": 0.0, "offsetYMicrometers": 30.0, "angleDegrees": 180.0},
                    {"name": "b0", "offsetXMicrometers": 250.0, "offsetYMicrometers": 22.0, "angleDegrees": 0.0},
                    {"name": "b1", "offsetXMicrometers": 250.0, "offsetYMicrometers": 30.0, "angleDegrees": 0.0},
                ],
                "sMatrix": None,
            },
            {
                "name": "io",
                "category": "I/O",
                "nazcaFunction": "io",
                "nazcaParameters": "",
                "widthMicrometers": 100,
                "heightMicrometers": 19,
                "nazcaOriginOffsetX": 0.0,
                "nazcaOriginOffsetY": 9.5,
                "pins": [
                    {"name": "a0", "offsetXMicrometers": 0.0, "offsetYMicrometers": 9.5, "angleDegrees": 180.0},
                    {"name": "b0", "offsetXMicrometers": 100.0, "offsetYMicrometers": 9.5, "angleDegrees": 0.0},
                ],
                "sMatrix": None,
            },
        ],
    }


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> None:
    args = sys.argv[1:]

    if not args or args[0] in ("-h", "--help"):
        print(__doc__, file=sys.stderr)
        print("\nAvailable modes:", file=sys.stderr)
        print("  python3 scripts/parse_pdk.py demo             # show reference demo values", file=sys.stderr)
        print("  python3 scripts/parse_pdk.py <module>         # parse all functions", file=sys.stderr)
        print("  python3 scripts/parse_pdk.py <module> f1 f2   # parse specific functions", file=sys.stderr)
        sys.exit(0)

    module_name = args[0]
    function_names = args[1:] if len(args) > 1 else None

    if module_name == "demo":
        result = demo_output()
    else:
        result = parse_pdk(module_name, function_names)

    # Write JSON to stdout (redirect to file to create PDK JSON)
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
