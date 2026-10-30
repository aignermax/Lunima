using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Configured port shapes provide positions, widths, kinds, and duplicate suppression.</summary>
public class GdsPinDetectorPortShapeTests
{
    [Fact]
    public void PortShape_Pin_CarriesThePortShapesLayer()
    {
        var cell = Cell(Poly(1, 10, (0, 1.5), (0.5, 1.5), (0.5, 2.5), (0, 2.5), (0, 1.5)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.PortShape);
        pin.Layer.ShouldBe(1);
    }

    [Fact]
    public void PortShape_OnConfiguredLayer_NoLabel_BecomesHeurPinWithWidthAndDirection()
    {
        // 1 µm wide port-marker polygon on the default port layer (1,10) touches
        // the left edge; without a label it becomes a named PortShape pin.
        var cell = Cell(Poly(1, 10, (0, 1.5), (0.5, 1.5), (0.5, 2.5), (0, 2.5), (0, 1.5)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.PortShape);
        pin.Name.ShouldBe("heur_1");
        pin.XUm.ShouldBe(0, Tolerance);
        pin.YUm.ShouldBe(2, Tolerance);
        pin.AngleDegrees.ShouldBe(180, Tolerance);
        pin.WidthUm.ShouldBe(1, Tolerance);
    }

    [Fact]
    public void PortShape_LabelNearby_AdoptsMidpointAndWidth()
    {
        // A label sitting on the marker polygon gets the shape's exact boundary
        // midpoint and width; the shape itself is not emitted separately.
        var cell = Cell(
            Poly(1, 10, (0, 1.5), (0.5, 1.5), (0.5, 2.5), (0, 2.5), (0, 1.5)),
            Label(1, 10, "o1", x: 0.2, y: 2));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.Label);
        pin.Name.ShouldBe("o1");
        pin.XUm.ShouldBe(0, Tolerance);
        pin.YUm.ShouldBe(2, Tolerance);
        pin.WidthUm.ShouldBe(1, Tolerance);
        pin.AngleDegrees.ShouldBe(180, Tolerance);
    }

    [Fact]
    public void PortShape_ElectricalLayer_InfersElectrical()
    {
        var options = new GdsPinDetectionOptions
        {
            PortLayers = [(11, 0)],
            ElectricalLayers = [(11, 0)],
        };
        var cell = Cell(Poly(11, 0, (0, 1.5), (0.5, 1.5), (0.5, 2.5), (0, 2.5), (0, 1.5)));

        var pins = GdsPinDetector.Detect(cell, Box10x4, options);

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.PortShape);
        pin.IsElectrical.ShouldBe(true);
    }

    [Fact]
    public void PortShape_NonPortLayer_IsIgnored()
    {
        var cell = Cell(Poly(2, 0, (0, 1.5), (0.5, 1.5), (0.5, 2.5), (0, 2.5), (0, 1.5)));

        GdsPinDetector.Detect(cell, Box10x4).ShouldBeEmpty();
    }

    [Fact]
    public void PortShape_AdjacentOnSameEdge_AreMerged()
    {
        // Two abutting port markers on the left edge represent one physical port.
        var cell = Cell(
            Poly(1, 10, (0, 1), (0.5, 1), (0.5, 2), (0, 2), (0, 1)),
            Poly(1, 10, (0, 2), (0.5, 2), (0.5, 3), (0, 3), (0, 2)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.Count.ShouldBe(1);
        pins[0].Source.ShouldBe(DetectedPinSource.PortShape);
        pins[0].WidthUm.ShouldBe(2, Tolerance);
    }

    [Fact]
    public void PortShape_OffCenter_YFlipConvertsGdsYToAppY()
    {
        // Off-center marker: GDS midpoint y = 1 in a 0..4 box must land at
        // app y = MaxY − gdsY = 3, pinning the Y-flip (a vertically centered
        // fixture would pass even with the flip missing).
        var cell = Cell(Poly(1, 10, (0, 0.5), (0.5, 0.5), (0.5, 1.5), (0, 1.5), (0, 0.5)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.PortShape);
        pin.XUm.ShouldBe(0, Tolerance);
        pin.YUm.ShouldBe(3, Tolerance);
        pin.AngleDegrees.ShouldBe(180, Tolerance);
        pin.WidthUm.ShouldBe(1, Tolerance);
    }

    [Fact]
    public void PortShape_CoversWaveguideEnd_SuppressesDuplicateHeuristicPin()
    {
        // A port marker on the left end plus a waveguide stub ending at the right
        // edge: the marker consumes the left end, the heuristic still fills the
        // uncovered right end.
        var cell = Cell(
            Poly(1, 0, (7, 1.75), (10, 1.75), (10, 2.25), (7, 2.25), (7, 1.75)),
            Poly(1, 10, (0, 1.5), (0.5, 1.5), (0.5, 2.5), (0, 2.5), (0, 1.5)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.Count.ShouldBe(2);
        pins[0].Source.ShouldBe(DetectedPinSource.PortShape);
        pins[0].XUm.ShouldBe(0, Tolerance);
        pins[1].Source.ShouldBe(DetectedPinSource.EdgeHeuristic);
        pins[1].XUm.ShouldBe(10, Tolerance);
    }
}
