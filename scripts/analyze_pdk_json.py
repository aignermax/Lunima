"""
Analyze PDK component definitions from a JSON file.
Issue #334: Investigate PDK JSON vs Nazca Python coordinate mismatch.

Reads a PDK JSON file and prints component dimensions, pin positions,
and the NazcaOriginOffset that would be derived by ConvertPdkComponentToTemplate().

Usage:
    python scripts/analyze_pdk_json.py [pdk_file.json]
    python scripts/analyze_pdk_json.py CAP-DataAccess/PDKs/demo-pdk.json

Output:
    For each component:
      - Name, category, nazcaFunction
      - Width x Height (µm)
      - Derived NazcaOriginOffset (from first pin — may be wrong!)
      - Pin list: name, (x, y), angle°
"""

import json
import sys
from pathlib import Path


def analyze_component(comp: dict) -> None:
    """Print detailed info for a single component."""
    name = comp.get("name", "?")
    category = comp.get("category", "?")
    nazca_func = comp.get("nazcaFunction", "<none>")
    width = comp.get("widthMicrometers", 0)
    height = comp.get("heightMicrometers", 0)
    pins = comp.get("pins", [])

    print(f"\nComponent: {name}")
    print(f"  Category:      {category}")
    print(f"  NazcaFunction: {nazca_func}")
    print(f"  Dimensions:    {width} x {height} µm")

    # Replicate ConvertPdkComponentToTemplate() logic from LeftPanelViewModel.cs
    if pins:
        first = pins[0]
        origin_x = first.get("offsetXMicrometers", 0)
        origin_y = first.get("offsetYMicrometers", 0)
        print(f"  Derived NazcaOriginOffset: ({origin_x}, {origin_y})  [from first pin '{first.get('name','?')}']")

        # Risk flag: if first pin is NOT at the logical Nazca origin (a0),
        # the derived offset may be wrong and cause coordinate mismatches.
        if first.get("name", "") not in ("a0", "in", "waveguide"):
            print(f"  *** WARNING: First pin '{first.get('name','')}' may not be the Nazca origin pin!")
    else:
        print(f"  Derived NazcaOriginOffset: (0, 0)  [no pins!]")
        print(f"  *** ERROR: Component has no pins!")

    print(f"  Pins ({len(pins)}):")
    for pin in pins:
        px = pin.get("offsetXMicrometers", 0)
        py = pin.get("offsetYMicrometers", 0)
        pa = pin.get("angleDegrees", 0)
        pname = pin.get("name", "?")

        # Bounds check
        within_x = -1.0 <= px <= width + 1.0
        within_y = -1.0 <= py <= height + 1.0
        flag = "" if (within_x and within_y) else "  *** OUT OF BOUNDS"
        print(f"    - {pname:12s}: ({px:7.2f}, {py:7.2f}) @ {pa:5.1f}°{flag}")


def analyze_pdk_file(pdk_path: str) -> None:
    """Load and analyze a PDK JSON file."""
    path = Path(pdk_path)
    if not path.exists():
        print(f"ERROR: File not found: {pdk_path}", file=sys.stderr)
        sys.exit(1)

    with open(path) as f:
        pdk = json.load(f)

    name = pdk.get("name", "Unknown PDK")
    version = pdk.get("version", "?")
    nazca_module = pdk.get("nazcaModuleName", "<none>")
    components = pdk.get("components", [])

    print("=" * 70)
    print(f"PDK: {name}  (v{version})")
    print(f"NazcaModule: {nazca_module}")
    print(f"Components: {len(components)}")
    print("=" * 70)

    for comp in components:
        analyze_component(comp)

    print("\n" + "=" * 70)
    print("Legend:")
    print("  Derived NazcaOriginOffset = origin used in Nazca .put(x, y) calls")
    print("  *** WARNING = first pin may not be Nazca origin — check manually")
    print("  *** OUT OF BOUNDS = pin outside component bounding box (1µm tolerance)")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        # Default: analyze demo-pdk.json
        default_path = Path(__file__).parent.parent / "CAP-DataAccess" / "PDKs" / "demo-pdk.json"
        print(f"No file specified. Using: {default_path}")
        analyze_pdk_file(str(default_path))
    else:
        analyze_pdk_file(sys.argv[1])
