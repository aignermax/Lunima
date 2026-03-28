"""
Reference Nazca script for GDS position verification (Issue #329).

Hand-written ground truth for a minimal design:
  - GC1 (Grating Coupler) at editor position (0, 0), unrotated
  - GC2 (Grating Coupler) at editor position (200, 0), unrotated
  - Straight waveguide connecting GC1.waveguide to GC2.waveguide

Design spec based on CAP system coordinate mapping:
  - Editor GC at (px, py) maps to Nazca placement at (px, -(py + 9.5))
  - Grating Coupler: 100×19 µm, waveguide pin at local (100, 9.5) in Nazca coords
  - Global Nazca pin positions:
      GC1.waveguide = (0 + 100, -(0 + 9.5) + 9.5) = (100,  0)
      GC2.waveguide = (200 + 100, -(0 + 9.5) + 9.5) = (300, 0)
  - Waveguide: straight from (100, 0) to (300, 0), length = 200 µm, angle = 0°

Run this script to produce the reference GDS:
  python Scripts/reference_minimal.py

Output: /tmp/reference_minimal.gds
"""
import os
import sys
import nazca as nd
import nazca.demofab as demo

nd.print_warning = False

# Waveguide width used by Nazca demofab demofab shallow guides
WG_WIDTH = 0.45  # µm

# ── Component stub definitions (mirrors what CAP SimpleNazcaExporter generates) ──
#
# The CAP system generates stubs like:
#   with nd.Cell(name='demo.io') as _demo_io_cell:
#       nd.Polygon(points=[(0,0),(100,0),(100,19),(0,19)], layer=1).put(0, 0)
#       nd.Pin('waveguide').put(100, 9.50, 0)
#   def demo_io(**kwargs):
#       return _demo_io_cell
#
# And places the component at (nazcaX, nazcaY, rot) where:
#   nazcaX = physicalX + originOffsetX = physicalX + 0
#   nazcaY = -(physicalY + originOffsetY) = -(physicalY + 9.5)
#   rot    = -rotationDegrees

with nd.Cell(name='GratingCoupler_stub') as _gc_cell:
    """Auto-generated stub for demo.io (100x19 µm)."""
    nd.Polygon(points=[(0, 0), (100, 0), (100, 19), (0, 19)], layer=1).put(0, 0)
    # Waveguide pin: local Nazca Y = height - editorOffsetY = 19 - 9.5 = 9.5
    nd.Pin('waveguide').put(100, 9.50, 0)


def GratingCoupler_stub(**kwargs):
    """Return the grating coupler stub cell."""
    return _gc_cell


# ── Reference design ─────────────────────────────────────────────────────────────

def create_reference_design():
    """
    Place 2 Grating Couplers and 1 straight waveguide using the SAME coordinate
    mapping that CAP's SimpleNazcaExporter should produce.

    Expected Nazca placement coordinates:
      GC1: physicalX=0,   physicalY=0 → Nazca (0,   -9.5, 0)
      GC2: physicalX=200, physicalY=0 → Nazca (200, -9.5, 0)

    Expected global pin positions:
      GC1.waveguide = (100,  0)   [local (100, 9.5) + placement (0, -9.5)]
      GC2.waveguide = (300,  0)   [local (100, 9.5) + placement (200, -9.5)]

    Correct waveguide: nd.strt(length=200).put(100, 0, 0)
    """
    with nd.Cell(name='Reference_MinimalDesign') as design:
        # Place both Grating Couplers
        comp_0 = GratingCoupler_stub().put(0,   -9.5, 0)   # GC1
        comp_1 = GratingCoupler_stub().put(200, -9.5, 0)   # GC2

        # Straight waveguide: starts at GC1.waveguide global pos (100, 0)
        # Angle 0° (pointing right) — matches pin orientation
        nd.strt(length=200, width=WG_WIDTH, layer=1).put(100, 0, 0)

    return design


# ── Export ────────────────────────────────────────────────────────────────────────

if __name__ == '__main__':
    design = create_reference_design()
    design.put()

    output_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               '..', 'tmp', 'reference_minimal.gds')
    output_path = os.path.normpath(output_path)
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    nd.export_gds(filename=output_path)
    print(f'Reference GDS exported to: {output_path}')
