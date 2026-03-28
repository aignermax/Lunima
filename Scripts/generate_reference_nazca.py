"""
Ground-truth reference script for Connect-A-PIC GDS export validation.
Creates a minimal test design with explicit, known coordinates.
Used to compare against Grid->Nazca export (Issue #329 debugging).

This script replicates EXACTLY what SimpleNazcaExporter should produce
for a two-component reference design:
  - Component 1: 100x50 µm box at physical (0, 0)
  - Component 2: 100x50 µm box at physical (300, 0)
  - Waveguide: straight 200 µm connecting their output/input pins

Nazca coordinate conventions (same as SimpleNazcaExporter):
  - Nazca Y = -(PhysicalY + HeightOffset)
  - Pin stub Y = Height - OffsetY  (Y-flip within stub)

Usage:
    python generate_reference_nazca.py <output.gds> <output_coords.json>
"""
import nazca as nd
import json
import sys

# ─── Reference design constants (micrometers) ────────────────────────────────
# These MUST match NazcaReferenceGenerator.cs constants exactly.
COMP_WIDTH   = 100.0
COMP_HEIGHT  = 50.0
COMP1_X      = 0.0
COMP1_Y      = 0.0
COMP2_X      = 300.0
COMP2_Y      = 0.0
PIN_OFFSET_X = 100.0   # 'out' pin at right edge
PIN_OFFSET_Y = 25.0    # 'out'/'in' pin at mid-height
WG_LENGTH    = COMP2_X - COMP_WIDTH  # 200.0 µm straight waveguide

# Nazca placement coordinates (Y-flipped: nazca_y = -(physical_y + height))
NAZCA_COMP_HEIGHT_OFFSET = COMP_HEIGHT   # = 50.0
NAZCA_COMP1_Y = -(COMP1_Y + NAZCA_COMP_HEIGHT_OFFSET)   # -50.0
NAZCA_COMP2_Y = -(COMP2_Y + NAZCA_COMP_HEIGHT_OFFSET)   # -50.0

# Waveguide start: absolute pin position, Y-flipped
WG_START_X = COMP1_X + PIN_OFFSET_X          # 100.0
WG_START_Y = -(COMP1_Y + PIN_OFFSET_Y)       # -25.0
WG_START_ANGLE = 0.0


# ─── Component stub definition ───────────────────────────────────────────────

# Define the reference component cell once (matches SimpleNazcaExporter stub format)
with nd.Cell(name='reference_component') as _reference_component_cell:
    """Auto-generated stub for reference_component (100.0x50.0 µm)."""
    nd.Polygon(
        points=[(0, 0), (100.00, 0), (100.00, 50.00), (0, 50.00)],
        layer=1
    ).put(0, 0)
    # Pin Y in stub = COMP_HEIGHT - OffsetY  (Y-flip inside cell)
    nd.Pin('out').put(100.00, 25.00,   0)   # OffsetX=100, OffsetY=25, Angle=0
    nd.Pin('in').put(  0.00, 25.00, 180)   # OffsetX=0,   OffsetY=25, Angle=180


def reference_component(**kwargs):
    """Returns the shared reference_component cell (matches SimpleNazcaExporter stub)."""
    return _reference_component_cell


# ─── Design creation ─────────────────────────────────────────────────────────

def create_reference_design():
    """
    Create reference design replicating what SimpleNazcaExporter should produce.

    Returns:
        tuple: (cell, expected_coords) where expected_coords is a dict
               mapping coordinate names to [x, y] pairs in physical µm.
    """
    with nd.Cell('ReferenceDesign') as cell:
        # Components (Nazca coords: y = -(physical_y + height))
        comp_0 = reference_component().put(COMP1_X, NAZCA_COMP1_Y, 0)   # physical (0, 0)
        comp_1 = reference_component().put(COMP2_X, NAZCA_COMP2_Y, 0)   # physical (300, 0)

        # Waveguide (Nazca coords: y = -physical_y)
        nd.strt(length=WG_LENGTH).put(WG_START_X, WG_START_Y, WG_START_ANGLE)

    # Expected ground-truth in physical (editor) coordinates
    expected = {
        "comp1_position":  [COMP1_X,               COMP1_Y],
        "comp1_pin_out":   [COMP1_X + PIN_OFFSET_X, COMP1_Y + PIN_OFFSET_Y],
        "comp1_pin_in":    [COMP1_X,               COMP1_Y + PIN_OFFSET_Y],
        "comp2_position":  [COMP2_X,               COMP2_Y],
        "comp2_pin_out":   [COMP2_X + PIN_OFFSET_X, COMP2_Y + PIN_OFFSET_Y],
        "comp2_pin_in":    [COMP2_X,               COMP2_Y + PIN_OFFSET_Y],
        "waveguide_start": [COMP1_X + PIN_OFFSET_X, COMP1_Y + PIN_OFFSET_Y],
        "waveguide_end":   [COMP2_X,               COMP2_Y + PIN_OFFSET_Y],
        "waveguide_length": WG_LENGTH,
        # Nazca internal coordinates (for GDS comparison)
        "nazca_comp1_x":   COMP1_X,
        "nazca_comp1_y":   NAZCA_COMP1_Y,
        "nazca_comp2_x":   COMP2_X,
        "nazca_comp2_y":   NAZCA_COMP2_Y,
        "nazca_wg_start_x": WG_START_X,
        "nazca_wg_start_y": WG_START_Y,
    }
    return cell, expected


# ─── Entry point ─────────────────────────────────────────────────────────────

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python generate_reference_nazca.py <output.gds> <output_coords.json>")
        sys.exit(1)

    gds_path    = sys.argv[1]
    coords_path = sys.argv[2]

    cell, expected = create_reference_design()

    nd.export_gds(topcells=cell, filename=gds_path)
    print(f"Reference GDS:    {gds_path}")

    with open(coords_path, 'w') as f:
        json.dump(expected, f, indent=2)
    print(f"Expected coords:  {coords_path}")

    print("\nGround-truth coordinates (physical µm):")
    for key, val in expected.items():
        print(f"  {key}: {val}")
