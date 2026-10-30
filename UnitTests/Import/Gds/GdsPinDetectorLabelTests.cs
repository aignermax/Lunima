using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Port-label filtering, layer attribution, and bounding-box edge directions.</summary>
public class GdsPinDetectorLabelTests
{
    [Fact]
    public void Label_OnPortLayerAtLeftEdge_ProducesWestOutwardPin()
    {
        var cell = Cell(Label(1, 10, "o1", x: 0, y: 3));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.Name.ShouldBe("o1");
        pin.Source.ShouldBe(DetectedPinSource.Label);
        pin.XUm.ShouldBe(0, Tolerance);
        pin.YUm.ShouldBe(1, Tolerance); // 4 − 3: Y flipped, origin at top
        pin.AngleDegrees.ShouldBe(180, Tolerance); // left edge → west
        pin.WidthUm.ShouldBe(0, Tolerance);
    }

    [Fact]
    public void Label_Pin_CarriesThePortLabelsLayer()
    {
        var cell = Cell(Label(1, 10, "o1", x: 0, y: 3));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldHaveSingleItem().Layer.ShouldBe(1);
    }

    [Fact]
    public void Label_SlightlyInsideLeftEdge_StillUsesThatEdge()
    {
        // Anchor within EdgeTouchToleranceUm of the edge: angle snaps to the
        // edge's outward normal, the position stays at the anchor.
        var cell = Cell(Label(1, 10, "o1", x: 0.0005, y: 2));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.AngleDegrees.ShouldBe(180, Tolerance);
        pin.XUm.ShouldBe(0.0005, Tolerance);
        pin.YUm.ShouldBe(2, Tolerance);
    }

    [Fact]
    public void Label_NotTouchingAnyEdge_UsesNearestEdge()
    {
        // Interior anchor, closest to the bottom edge (GDS MinY = visual bottom).
        var cell = Cell(Label(1, 10, "o1", x: 5, y: 0.5));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.AngleDegrees.ShouldBe(90, Tolerance); // bottom edge → down in app convention
        pin.XUm.ShouldBe(5, Tolerance);
        pin.YUm.ShouldBe(3.5, Tolerance); // 4 − 0.5
    }

    [Fact]
    public void Label_OnTopAndBottomEdges_TopIs270BottomIs90()
    {
        // GDS MaxY is the visual top edge (appY = 0): outward = up = 270°.
        // GDS MinY is the visual bottom edge: outward = down = 90°.
        var cell = Cell(
            Label(1, 10, "top", x: 5, y: 4),
            Label(1, 10, "bottom", x: 5, y: 0));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.Count.ShouldBe(2);
        pins[0].Name.ShouldBe("top"); // top edge sorts before bottom edge
        pins[0].AngleDegrees.ShouldBe(270, Tolerance);
        pins[0].YUm.ShouldBe(0, Tolerance);
        pins[1].Name.ShouldBe("bottom");
        pins[1].AngleDegrees.ShouldBe(90, Tolerance);
        pins[1].YUm.ShouldBe(4, Tolerance);
    }

    [Fact]
    public void Label_OnNonPortLayer_IsIgnored()
    {
        var cell = Cell(
            Label(2, 10, "wrong-layer", x: 0, y: 2),
            Label(1, 11, "wrong-texttype", x: 0, y: 3));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldBeEmpty();
    }

    [Fact]
    public void AnchorAndParameterLabels_OnConfiguredPortLayer_NeverBecomePins()
    {
        // nazca stamps bbox anchors (tl/tr/bc/…) and parameter annotations on
        // text layers — possibly the same layer the real port labels live on.
        // Even with that layer configured as a port layer, they are never
        // ports (GdsGhostLabelFilter applies on every path, not just the
        // any-layer fallback).
        var cell = Cell(
            Label(1, 10, "o1", x: 0, y: 2),
            Label(1, 10, "tl", x: 0, y: 4),
            Label(1, 10, "br", x: 10, y: 0),
            Label(1, 10, "R:0.0001", x: 5, y: 2),
            Label(1, 10, "n=1.0", x: 5, y: 3));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.Name.ShouldBe("o1");
    }
}
