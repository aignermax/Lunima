using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Nazca arrow-pair positions, directions, and nearby-label adoption.</summary>
public class GdsPinDetectorArrowTests
{
    [Fact]
    public void ArrowPair_AtWaveguideEnd_GivesExactPinPositionAndOutwardAngle()
    {
        // Two chevrons whose tips meet at (0,2) mark the pin at the left end
        // of the waveguide. The marker layer is unconfigured: position comes
        // from the markers, direction/material from the probed geometry.
        var cell = Cell(
            Poly(1, 0, (0, 1.75), (10, 1.75), (10, 2.25), (0, 2.25), (0, 1.75)),
            Chevron(232, 0, 2, bodyDirX: +1),
            Chevron(232, 0, 2, bodyDirX: -1));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var arrowPin = pins.Single(p => p.Source == DetectedPinSource.ArrowMarker);
        arrowPin.XUm.ShouldBe(0, Tolerance);
        arrowPin.YUm.ShouldBe(2, Tolerance);
        arrowPin.AngleDegrees.ShouldBe(180, Tolerance); // left edge → west
        // the marker suppresses the edge heuristic at its position; the bare
        // right end still gets one:
        var heuristic = pins.Single(p => p.Source == DetectedPinSource.EdgeHeuristic);
        heuristic.XUm.ShouldBe(10, Tolerance);
        pins.Count.ShouldBe(2);
    }

    [Fact]
    public void Label_NearArrowPair_AdoptsTheExactTipPositionAndAngle()
    {
        // The label sits 0.8 µm inside the waveguide — alone, the probe would
        // read the core's top/bottom outline and report a wrong (perpendicular)
        // angle. The nearby marker pair re-anchors it to the pin tip.
        var cell = Cell(
            Poly(1, 0, (0, 1.75), (10, 1.75), (10, 2.25), (0, 2.25), (0, 1.75)),
            Chevron(232, 0, 2, bodyDirX: +1),
            Chevron(232, 0, 2, bodyDirX: -1),
            Label(1, 10, "o1", x: 0.8, y: 2));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.Single(p => p.Source == DetectedPinSource.Label);
        pin.Name.ShouldBe("o1");
        pin.XUm.ShouldBe(0, Tolerance);
        pin.YUm.ShouldBe(2, Tolerance);
        pin.AngleDegrees.ShouldBe(180, Tolerance);
        pins.ShouldNotContain(p => p.Source == DetectedPinSource.ArrowMarker,
            "the label pin already represents the marker's position");
    }

    [Fact]
    public void SingleChevron_EvenTouchingTheWaveguide_NeverBecomesAPin()
    {
        // Single chevrons are orientation markers in nazca conventions
        // (demofab devices carry four of them) — only tip-PAIRS mark pins.
        var cell = Cell(
            Poly(1, 0, (0, 1.75), (10, 1.75), (10, 2.25), (0, 2.25), (0, 1.75)),
            Chevron(232, 0, 2, bodyDirX: +1));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldNotContain(p => p.Source == DetectedPinSource.ArrowMarker);
        // the untouched waveguide ends still yield the usual heuristic pins:
        pins.Count(p => p.Source == DetectedPinSource.EdgeHeuristic).ShouldBe(2);
    }

    [Fact]
    public void ArrowPair_AcrossDifferentMarkerLayers_AlsoMarksAPin()
    {
        // Some conventions stamp one chevron per pin per marker layer: tips
        // meeting across two DIFFERENT layers still mark the pin.
        var cell = Cell(
            Poly(1, 0, (0, 1.75), (10, 1.75), (10, 2.25), (0, 2.25), (0, 1.75)),
            Chevron(232, 0, 2, bodyDirX: +1),
            Chevron(234, 0, 2, bodyDirX: -1));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var arrowPin = pins.Single(p => p.Source == DetectedPinSource.ArrowMarker);
        arrowPin.XUm.ShouldBe(0, Tolerance);
        arrowPin.YUm.ShouldBe(2, Tolerance);
        arrowPin.AngleDegrees.ShouldBe(180, Tolerance);
    }
}
