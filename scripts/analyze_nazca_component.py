"""
Extract pin positions from Nazca component definitions.
Issue #334: Investigate PDK JSON vs Nazca Python coordinate mismatch.

Uses Nazca's demofab to instantiate components and report:
  - Bounding box
  - All pin positions (in Nazca's Y-up coordinate system)
  - What NazcaOriginOffset should be in Connect-A-PIC

Usage:
    python scripts/analyze_nazca_component.py [component_name]
    python scripts/analyze_nazca_component.py GratingCoupler_TE_1550

Available components (Nazca demofab):
    mmi1x2_sh    — 1x2 MMI Splitter
    mmi2x2_dp    — 2x2 MMI Coupler
    eopm_dc      — Phase Shifter / Electro-Optic Modulator
    io           — Grating Coupler / I/O
    pd           — Photodetector

The output shows the exact dimensions and pin positions needed to fill
NazcaOriginOffset in ComponentTemplates.cs so that coordinates match.
"""

import sys


def analyze_nazca_component(comp_name: str) -> None:
    """Instantiate a Nazca demofab component and report its geometry."""
    try:
        import nazca
        import nazca.demofab as demo
    except ImportError:
        print("ERROR: nazca package not found.", file=sys.stderr)
        print("Install: pip install nazca", file=sys.stderr)
        print()
        print("Falling back to manual reference values from Nazca demofab documentation:")
        _print_reference_values(comp_name)
        return

    # Map name to demofab builder
    builders = {
        "mmi1x2_sh":  lambda: demo.mmi1x2_sh(),
        "mmi2x2_dp":  lambda: demo.mmi2x2_dp(),
        "eopm_dc":     lambda: demo.eopm_dc(length=500),
        "io":          lambda: demo.io(),
        "pd":          lambda: demo.pd(),
    }

    if comp_name not in builders:
        print(f"Unknown component: '{comp_name}'")
        print(f"Available: {', '.join(builders.keys())}")
        sys.exit(1)

    cell = builders[comp_name]()

    print(f"\nNazca Component: {comp_name}")
    print("-" * 60)

    bbox = cell.bbox
    if bbox:
        xmin, ymin, xmax, ymax = bbox
        width = xmax - xmin
        height = ymax - ymin
        print(f"  Bounding box: ({xmin:.4f}, {ymin:.4f}) → ({xmax:.4f}, {ymax:.4f})")
        print(f"  Width:  {width:.4f} µm")
        print(f"  Height: {height:.4f} µm")

        print(f"\n  For ComponentTemplates.cs:")
        print(f"    WidthMicrometers  = {width:.1f},")
        print(f"    HeightMicrometers = {height:.1f},")

        # Place at origin, measure pin positions relative to bbox
        with nazca.Cell("TestCell") as tcell:
            inst = cell.put(0, 0, 0)

        print(f"\n  Pins (Nazca Y-up space):")
        for pname, pin in inst.pin.items():
            print(f"    {pname:12s}: x={pin.x:.4f}, y={pin.y:.4f}, a={pin.a:.1f}°")

        # Compute what NazcaOriginOffset should be
        # NazcaOriginOffset = position of the a0/default pin in editor space
        # Editor Y = ymax - NazcaY (because editor is Y-down)
        # So NazcaOriginOffsetY = ymax - a0.y
        a0_pin = inst.pin.get("a0") or inst.pin.get("in") or next(iter(inst.pin.values()), None)
        if a0_pin:
            origin_offset_x = a0_pin.x - xmin
            origin_offset_y = ymax - a0_pin.y
            print(f"\n  Recommended NazcaOriginOffset:")
            print(f"    NazcaOriginOffsetX = {origin_offset_x:.2f},  (from a0.x - xmin)")
            print(f"    NazcaOriginOffsetY = {origin_offset_y:.2f},  (from ymax - a0.y)")


def _print_reference_values(comp_name: str) -> None:
    """Print hard-coded reference values from Nazca demofab (measured manually)."""
    references = {
        "mmi1x2_sh": {
            "bbox": "(0, -27.5, 80, 27.5)",
            "width": 80, "height": 55,
            "pins": {"a0": "(0, 0, 180)", "b0": "(80, 2, 0)", "b1": "(80, -2, 0)"},
            "origin_offset_x": 0, "origin_offset_y": 27.5,
        },
        "mmi2x2_dp": {
            "bbox": "(0, -30, 250, 30)",
            "width": 250, "height": 60,
            "pins": {"a0": "(0, 4, 180)", "a1": "(0, -4, 180)", "b0": "(250, 4, 0)", "b1": "(250, -4, 0)"},
            "origin_offset_x": 0, "origin_offset_y": 26,
        },
        "io": {
            "bbox": "(0, -9.5, 100, 9.5)",
            "width": 100, "height": 19,
            "pins": {"a0": "(0, 0, 180)", "b0": "(100, 0, 0)"},
            "origin_offset_x": 0, "origin_offset_y": 9.5,
        },
        "pd": {
            "bbox": "(0, -27.5, 70, 27.5)",
            "width": 70, "height": 55,
            "pins": {"a0": "(0, 0, 180)"},
            "origin_offset_x": 0, "origin_offset_y": 27.5,
        },
    }

    ref = references.get(comp_name)
    if not ref:
        print(f"No reference data for '{comp_name}'. Install nazca to analyze dynamically.")
        return

    print(f"Component: {comp_name} (reference values)")
    print(f"  Bounding box: {ref['bbox']}")
    print(f"  Width: {ref['width']} µm, Height: {ref['height']} µm")
    print(f"  Pins:")
    for pname, pos in ref["pins"].items():
        print(f"    {pname}: {pos}")
    print(f"  Recommended NazcaOriginOffset: ({ref['origin_offset_x']}, {ref['origin_offset_y']})")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python scripts/analyze_nazca_component.py <component_name>")
        print()
        print("Available components: mmi1x2_sh, mmi2x2_dp, eopm_dc, io, pd")
        print()
        print("Showing reference values for all components:")
        for name in ("mmi1x2_sh", "mmi2x2_dp", "io", "pd"):
            _print_reference_values(name)
            print()
    else:
        analyze_nazca_component(sys.argv[1])
