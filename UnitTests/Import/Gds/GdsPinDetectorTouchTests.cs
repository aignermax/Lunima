using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Adjacent channel ends, width filtering, and label overlap handling.</summary>
public class GdsPinDetectorTouchTests
{
    [Fact]
    public void Touches_AdjacentWithinTolerance_StaySeparateChannelEnds()
    {
        // Two waveguide faces on the left edge, 0.0005 µm apart (< tolerance).
        var cell = Cell(
            Poly(1, 0, (0, 1), (3, 1), (3, 2), (0, 2), (0, 1)),
            Poly(1, 0, (0, 2.0005), (3, 2.0005), (3, 3), (0, 3), (0, 2.0005)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        // Two parallel channels this close together are found as TWO separate
        // ports per side by the terminus scan (coupler-style cells need their
        // ports distinct); each channel exposes both its ends.
        pins.Count.ShouldBe(4);
        pins[0].XUm.ShouldBe(0, Tolerance);
        pins[0].YUm.ShouldBe(1.49975, Tolerance); // 4 − 2.50025
        pins[0].WidthUm.ShouldBe(0.9995, Tolerance);
        pins[0].AngleDegrees.ShouldBe(180, Tolerance);
        pins[1].YUm.ShouldBe(2.5, Tolerance);
        pins[1].WidthUm.ShouldBe(1, Tolerance);
        pins[2].XUm.ShouldBe(3, Tolerance);
        pins[2].AngleDegrees.ShouldBe(0, Tolerance);
        pins[3].XUm.ShouldBe(3, Tolerance);
        pins[3].AngleDegrees.ShouldBe(0, Tolerance);
    }

    [Fact]
    public void Touches_SeparatedBeyondTolerance_StaySeparate()
    {
        // Same faces, but 0.002 µm apart (> tolerance).
        var cell = Cell(
            Poly(1, 0, (0, 1), (3, 1), (3, 2), (0, 2), (0, 1)),
            Poly(1, 0, (0, 2.002), (3, 2.002), (3, 3), (0, 3), (0, 2.002)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        // Both channels stay separate — and each exposes both ends.
        pins.Count.ShouldBe(4);
        pins[0].WidthUm.ShouldBe(0.998, Tolerance);
        pins[1].WidthUm.ShouldBe(1, Tolerance);
        pins[2].WidthUm.ShouldBe(0.998, Tolerance);
        pins[3].WidthUm.ShouldBe(1, Tolerance);
    }

    [Fact]
    public void Touch_WiderThanMaxPinWidth_IsFiltered()
    {
        var box = new GdsBoundingBox(0, 0, 200, 200);
        var cell = Cell(Poly(1, 0, (0, 10), (10, 10), (10, 160), (0, 160), (0, 10))); // 150 µm long sides

        var pins = GdsPinDetector.Detect(cell, box);

        // The 150 µm long sides are sidewalls, not ports — rejected by the
        // sidewall rule (longer than the frame is deep). The block's two 10 µm
        // end faces are real channel ends and stay.
        pins.Count.ShouldBe(2);
        pins[0].AngleDegrees.ShouldBe(270, Tolerance); // top end
        pins[0].WidthUm.ShouldBe(10, Tolerance);
        pins[1].AngleDegrees.ShouldBe(90, Tolerance); // bottom end
        pins[1].WidthUm.ShouldBe(10, Tolerance);
    }

    [Fact]
    public void Touch_NarrowerThanMinPinWidth_IsFiltered()
    {
        var box = new GdsBoundingBox(0, 0, 200, 200);
        var cell = Cell(Poly(1, 0, (0, 10), (10, 10), (10, 10.05), (0, 10.05), (0, 10))); // 0.05 µm face

        var pins = GdsPinDetector.Detect(cell, box);

        pins.ShouldBeEmpty();
    }

    [Fact]
    public void LabelAndTouch_AtSameSpot_YieldsOnlyLabelPin()
    {
        var cell = Cell(
            Label(1, 10, "o1", x: 0, y: 2),
            Poly(1, 0, (0, 1.75), (3, 1.75), (3, 2.25), (0, 2.25), (0, 1.75)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.Label);
        pin.Name.ShouldBe("o1");
        pin.WidthUm.ShouldBe(0, Tolerance);
        pin.AngleDegrees.ShouldBe(180, Tolerance);
    }
}
