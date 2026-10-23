using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Waveguide end faces preserve layers, orientations, and exact tilted termini.</summary>
public class GdsPinDetectorWaveguideEdgeTests
{
    [Fact]
    public void EdgeHeuristic_Pins_CarryTheWaveguideLayer_WhenUnambiguous()
    {
        // Straight waveguide on layer 1/0 touching the left and right edges.
        var cell = Cell(Poly(1, 0, (0, 1.75), (10, 1.75), (10, 2.25), (0, 2.25), (0, 1.75)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldNotBeEmpty();
        pins.ShouldAllBe(p => p.Layer == 1);
    }

    [Fact]
    public void Waveguide_TouchingRightEdge_ProducesEastPinWithSegmentWidth()
    {
        // 1 µm tall waveguide end face on the right edge, GDS y ∈ [1, 2].
        var cell = Cell(Poly(1, 0, (8, 1), (10, 1), (10, 2), (8, 2), (8, 1)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        // A label-free channel stub exposes BOTH ends: the left face (west)
        // sorts before the right face (east) the name refers to.
        pins.Count.ShouldBe(2);
        pins[0].Source.ShouldBe(DetectedPinSource.EdgeHeuristic);
        pins[0].Name.ShouldBe("heur_1");
        pins[0].AngleDegrees.ShouldBe(180, Tolerance); // left face → west
        pins[0].XUm.ShouldBe(8, Tolerance);
        pins[0].YUm.ShouldBe(2.5, Tolerance);
        pins[0].WidthUm.ShouldBe(1, Tolerance);
        var pin = pins[1];
        pin.Source.ShouldBe(DetectedPinSource.EdgeHeuristic);
        pin.Name.ShouldBe("heur_2");
        pin.AngleDegrees.ShouldBe(0, Tolerance); // right edge → east
        pin.XUm.ShouldBe(10, Tolerance);
        pin.YUm.ShouldBe(2.5, Tolerance); // 4 − 1.5 (segment midpoint, Y flipped)
        pin.WidthUm.ShouldBe(1, Tolerance);
    }

    [Fact]
    public void Waveguide_TouchingTopAndBottomEdges_TopIs270BottomIs90()
    {
        var cell = Cell(
            Poly(1, 0, (4, 4), (6, 4), (6, 3), (4, 3), (4, 4)), // 2 µm face on GDS MaxY (visual top)
            Poly(1, 0, (4, 0), (6, 0), (6, 1), (4, 1), (4, 0))); // 2 µm face on GDS MinY (visual bottom)

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        // Label-free channel cells expose EVERY channel end: the two stubs' far
        // ends (left/right faces) join the two box-edge faces the name refers to.
        // Order: left edge, top edge, right edge, bottom edge.
        pins.Count.ShouldBe(6);
        pins[2].Name.ShouldBe("heur_3"); // top edge sorts after the left-edge pins
        pins[2].AngleDegrees.ShouldBe(270, Tolerance);
        pins[2].XUm.ShouldBe(5, Tolerance);
        pins[2].YUm.ShouldBe(0, Tolerance);
        pins[2].WidthUm.ShouldBe(2, Tolerance);
        pins[5].Name.ShouldBe("heur_6"); // bottom edge sorts last
        pins[5].AngleDegrees.ShouldBe(90, Tolerance);
        pins[5].XUm.ShouldBe(5, Tolerance);
        pins[5].YUm.ShouldBe(4, Tolerance);
        pins[5].WidthUm.ShouldBe(2, Tolerance);
    }

    [Fact]
    public void Polygon_OnNonWaveguideLayer_IsIgnored()
    {
        var cell = Cell(
            Poly(2, 0, (0, 1), (3, 1), (3, 2), (0, 2), (0, 1)),
            Poly(1, 5, (0, 3), (3, 3), (3, 3.5), (0, 3.5), (0, 3)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldBeEmpty();
    }

    [Fact]
    public void MetalPolygon_TouchingEdge_ProducesNoHeuristicPins()
    {
        // The edge heuristic only scans waveguide layers; metal-only edges are
        // intentionally out of scope (electrical ports need labels or a
        // higher-confidence detector).
        var cell = Cell(Poly(11, 0, (0, 1), (3, 1), (3, 2), (0, 2), (0, 1)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldBeEmpty();
    }

    [Fact]
    public void ClosingPointDuplication_CreatesNoPhantomPins()
    {
        var cell = Cell(
            // Standard closed rect (first point repeated): one left-edge face, width 1.
            Poly(1, 0, (0, 1), (3, 1), (3, 2), (0, 2), (0, 1)),
            // Extra consecutive duplicate vertex ON the edge: zero-length touch must vanish.
            Poly(1, 0, (0, 3), (0, 3), (3, 3), (3, 3.5), (0, 3.5), (0, 3)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        // No phantom pins from the duplicated vertex — and each label-free
        // channel stub exposes both of its ends (left AND right faces).
        pins.Count.ShouldBe(4);
        pins[0].Name.ShouldBe("heur_1"); // left edge, smaller appY sorts first
        pins[0].YUm.ShouldBe(0.75, Tolerance); // 4 − 3.25
        pins[0].WidthUm.ShouldBe(0.5, Tolerance);
        pins[1].Name.ShouldBe("heur_2");
        pins[1].YUm.ShouldBe(2.5, Tolerance); // 4 − 1.5
        pins[1].WidthUm.ShouldBe(1, Tolerance);
        pins[2].AngleDegrees.ShouldBe(0, Tolerance); // right edge faces east
        pins[2].WidthUm.ShouldBe(0.5, Tolerance);
        pins[3].AngleDegrees.ShouldBe(0, Tolerance);
        pins[3].WidthUm.ShouldBe(1, Tolerance);
    }

    [Fact]
    public void TerminusFace_TiltedExitBehindEnvelope_FoundWithExactAngle()
    {
        // gdsfactory partial-angle bend (94.02° sweep): the exit face is tilted
        // 4° past every axis-aligned bbox edge, and the layer-68 envelope
        // inflates the cell bbox 0.5 µm past the core — the edge-touch scan
        // alone loses the exit pin and breaks the chain (field report: rotated
        // fan-out bends never connected to their continuation straights).
        var core = AnnulusSector(1, 0, centerX: 0, centerY: 0,
            radiusOuter: 20.25, radiusInner: 19.75, fromDegrees: 180, toDegrees: 274.02);
        var envelope = Poly(68, 0,
            (-20.75, -20.6982), (1.9189, -20.6982), (1.9189, 0.5), (-20.75, 0.5), (-20.75, -20.6982));
        var cell = Cell(core, envelope);
        var box = new GdsBoundingBox(-20.75, -20.6982, 1.9189, 0.5);

        var pins = GdsPinDetector.Detect(cell, box);

        pins.Count.ShouldBe(2);
        // Entry face (axis-aligned): found by the edge scan on the waveguide frame.
        pins[0].XUm.ShouldBe(0.75, 1e-3);
        pins[0].YUm.ShouldBe(0.5, 1e-3);
        pins[0].AngleDegrees.ShouldBe(270, 0.3);
        // Exit face (tilted 4°): only the terminus ring scan sees it — exact
        // position and the true travel direction (app 355.98°).
        pins[1].XUm.ShouldBe(22.1514, 1e-3);
        pins[1].YUm.ShouldBe(20.4498, 1e-3);
        pins[1].AngleDegrees.ShouldBe(355.98, 0.3);
    }
}
