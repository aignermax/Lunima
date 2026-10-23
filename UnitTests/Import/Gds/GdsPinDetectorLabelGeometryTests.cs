using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Label directions follow local geometry with a bounding-box fallback.</summary>
public class GdsPinDetectorLabelGeometryTests
{
    [Fact]
    public void Label_OnWaveguideEndFaceInsideLargeBBox_UsesSegmentNormal_AllOrientations()
    {
        // A 100 × 80 µm black-box-sized cell whose four waveguide stubs end deep
        // inside the bbox, each label sitting ON its stub's end face. The bbox
        // edge rule would point every pin at the nearest outer edge; the local
        // geometry must win and point along the waveguide axis instead.
        var box = new GdsBoundingBox(0, 0, 100, 80);
        var cell = Cell(
            // East-pointing stub: end face x = 14, body extending to -X.
            Poly(1, 0, (10, 39.75), (14, 39.75), (14, 40.25), (10, 40.25), (10, 39.75)),
            Label(1, 10, "east", x: 14, y: 40),
            // West-pointing stub: end face x = 86, body extending to +X.
            Poly(1, 0, (86, 39.75), (90, 39.75), (90, 40.25), (86, 40.25), (86, 39.75)),
            Label(1, 10, "west", x: 86, y: 40),
            // Down-pointing stub (app convention): body extending to GDS +Y.
            Poly(1, 0, (49.75, 60), (50.25, 60), (50.25, 64), (49.75, 64), (49.75, 60)),
            Label(1, 10, "down", x: 50, y: 60),
            // Up-pointing stub (app convention): body extending to GDS -Y.
            Poly(1, 0, (49.75, 16), (50.25, 16), (50.25, 20), (49.75, 20), (49.75, 16)),
            Label(1, 10, "up", x: 50, y: 20));

        var pins = GdsPinDetector.Detect(cell, box);

        pins.Count.ShouldBe(4);
        // Sorted by nearest bbox edge (left, top, right, bottom): east → left,
        // down → top, west → right, up → bottom.
        pins[0].Name.ShouldBe("east");
        pins[0].AngleDegrees.ShouldBe(0, Tolerance);
        pins[1].Name.ShouldBe("down");
        pins[1].AngleDegrees.ShouldBe(90, Tolerance);
        pins[2].Name.ShouldBe("west");
        pins[2].AngleDegrees.ShouldBe(180, Tolerance);
        pins[3].Name.ShouldBe("up");
        pins[3].AngleDegrees.ShouldBe(270, Tolerance);
    }

    [Fact]
    public void Label_OnDiagonalSegment_FlipsYIntoAppConvention()
    {
        // 45° face with GDS-space outward normal (+1, +1)/√2 (NE). Without the
        // Y-flip this would come out as 45°; the app convention (Y-down) must
        // mirror it to 315°.
        var cell = Cell(
            Poly(1, 0, (4, 4), (6, 4), (4, 6), (4, 4)),
            Label(1, 10, "diag", x: 5.05, y: 5.05));

        var pins = GdsPinDetector.Detect(cell, new GdsBoundingBox(0, 0, 10, 10));

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.Label);
        pin.AngleDegrees.ShouldBe(315, Tolerance);
    }

    [Fact]
    public void Label_FarFromGeometry_FallsBackToBoundingBoxEdge()
    {
        // Waveguide stub ends at x = 3, the label floats 2 µm past its end face
        // — beyond LabelGeometryTouchToleranceUm (1.0), so the bbox rule applies.
        var cell = Cell(
            Poly(1, 0, (1, 1.75), (3, 1.75), (3, 2.25), (1, 2.25), (1, 1.75)),
            Label(1, 10, "o1", x: 5, y: 3));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        var pin = pins.ShouldHaveSingleItem();
        pin.AngleDegrees.ShouldBe(270, Tolerance); // nearest bbox edge: top (GDS MaxY)
        pin.IsElectrical.ShouldBeNull("no polygon near, and 'o1' is no electrical name");
    }

    [Fact]
    public void Label_OnMetalPolygonEndFaceInsideLargeBBox_UsesMetalSegmentNormal()
    {
        // Metal pads count as direction geometry too: the label sits on the pad's
        // right end face, deep inside the bbox.
        var box = new GdsBoundingBox(0, 0, 100, 80);
        var cell = Cell(
            Poly(11, 0, (10, 39.75), (14, 39.75), (14, 40.25), (10, 40.25), (10, 39.75)),
            Label(1, 10, "anode", x: 14, y: 40));

        var pins = GdsPinDetector.Detect(cell, box);

        var pin = pins.ShouldHaveSingleItem();
        pin.AngleDegrees.ShouldBe(0, Tolerance);
    }
}
