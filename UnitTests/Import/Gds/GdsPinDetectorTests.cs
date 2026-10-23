using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Detector options, empty inputs, and deterministic pin ordering.</summary>
public class GdsPinDetectorTests
{
    [Fact]
    public void CustomLayers_AreRespected()
    {
        var options = new GdsPinDetectionOptions
        {
            PortLayers = [(3, 0)],
            WaveguideLayers = [(7, 1)],
        };
        var cell = Cell(
            Label(3, 0, "p1", x: 0, y: 2),
            Label(1, 10, "ignored", x: 0, y: 3),
            Poly(7, 1, (8, 1), (10, 1), (10, 2), (8, 2), (8, 1)),
            Poly(1, 0, (0, 1), (3, 1), (3, 2), (0, 2), (0, 1)));

        var pins = GdsPinDetector.Detect(cell, Box10x4, options);

        pins.Count.ShouldBe(2);
        pins[0].Name.ShouldBe("p1");
        pins[0].Source.ShouldBe(DetectedPinSource.Label);
        pins[1].Name.ShouldBe("heur_1");
        pins[1].AngleDegrees.ShouldBe(0, Tolerance);
    }

    [Fact]
    public void CustomWidthBounds_AreRespected()
    {
        var box = new GdsBoundingBox(0, 0, 200, 200);
        var wideCell = Cell(Poly(1, 0, (0, 10), (10, 10), (10, 160), (0, 160), (0, 10)));
        var narrowCell = Cell(Poly(1, 0, (0, 10), (10, 10), (10, 10.05), (0, 10.05), (0, 10)));

        // The block's 150 µm long sides are sidewalls (rejected by the sidewall
        // rule no matter how high the custom max width goes); its two 10 µm end
        // faces are channel ends and respect the custom window.
        var widePins = GdsPinDetector.Detect(wideCell, box,
            new GdsPinDetectionOptions { MaxPinWidthUm = 200 });
        widePins.Count.ShouldBe(2);
        widePins.ShouldAllBe(p => Math.Abs(p.WidthUm - 10) < Tolerance);
        GdsPinDetector.Detect(wideCell, box, new GdsPinDetectionOptions { MaxPinWidthUm = 5 })
            .ShouldBeEmpty("the 10 µm end faces exceed the custom max width");

        // The 0.05 µm end faces pass only with the lowered minimum.
        var narrowPins = GdsPinDetector.Detect(narrowCell, box,
            new GdsPinDetectionOptions { MinPinWidthUm = 0.01 });
        narrowPins.Count.ShouldBe(2);
        narrowPins.ShouldAllBe(p => Math.Abs(p.WidthUm - 0.05) < Tolerance);
        GdsPinDetector.Detect(narrowCell, box)
            .ShouldBeEmpty("the 0.05 µm end faces fall below the default min width");
    }

    [Fact]
    public void EmptyCell_ReturnsEmptyList()
    {
        var pins = GdsPinDetector.Detect(Cell(), Box10x4);

        pins.ShouldBeEmpty();
    }

    [Fact]
    public void DegenerateBoundingBox_ReturnsEmptyList()
    {
        var cell = Cell(
            Label(1, 10, "o1", x: 0, y: 2),
            Poly(1, 0, (0, 1), (3, 1), (3, 2), (0, 2), (0, 1)));

        GdsPinDetector.Detect(cell, GdsBoundingBox.Empty).ShouldBeEmpty();
        GdsPinDetector.Detect(cell, new GdsBoundingBox(0, 0, 0, 4)).ShouldBeEmpty();
        GdsPinDetector.Detect(cell, new GdsBoundingBox(0, 0, 10, 0)).ShouldBeEmpty();
    }

    [Fact]
    public void Pins_AreSortedByEdgeThenPosition_HeuristicNamesAssignedAfterSorting()
    {
        var box = new GdsBoundingBox(0, 0, 10, 10);
        var cell = Cell(
            Poly(1, 0, (0, 2), (2, 2), (2, 2.5), (0, 2.5), (0, 2)),       // left, appY 7.75
            Poly(1, 0, (0, 8), (2, 8), (2, 8.5), (0, 8.5), (0, 8)),       // left, appY 1.75
            Label(1, 10, "o1", x: 0, y: 5),                               // left, appY 5
            Poly(1, 0, (4, 10), (5, 10), (5, 9), (4, 9), (4, 10)),        // top
            Poly(1, 0, (10, 4), (10, 5), (9, 5), (9, 4), (10, 4)),        // right
            Poly(1, 0, (3, 0), (4, 0), (4, 1), (3, 1), (3, 0)));          // bottom

        var pins = GdsPinDetector.Detect(cell, box);

        pins.Count.ShouldBe(6);
        // Edge order: left, top, right, bottom; within an edge by app-space position.
        pins[0].Name.ShouldBe("heur_1");
        pins[0].YUm.ShouldBe(1.75, Tolerance);
        pins[1].Name.ShouldBe("o1"); // label keeps its name and consumes no heur_ number
        pins[1].YUm.ShouldBe(5, Tolerance);
        pins[2].Name.ShouldBe("heur_2");
        pins[2].YUm.ShouldBe(7.75, Tolerance);
        pins[3].Name.ShouldBe("heur_3");
        pins[3].AngleDegrees.ShouldBe(270, Tolerance);
        pins[4].Name.ShouldBe("heur_4");
        pins[4].AngleDegrees.ShouldBe(0, Tolerance);
        pins[5].Name.ShouldBe("heur_5");
        pins[5].AngleDegrees.ShouldBe(90, Tolerance);
        pins[5].YUm.ShouldBe(10, Tolerance);
    }
}
