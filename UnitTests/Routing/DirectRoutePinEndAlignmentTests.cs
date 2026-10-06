using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Tests for the pin-end axis alignment of degenerate direct routes (issue #1363):
/// between facing pins with a sub-bend-radius lateral offset the styled candidate is a
/// single diagonal straight whose tilted end caps poke a rounding sliver past the
/// partner components' DevRec planes on export (SiEPIC "Overlapping component"). The
/// router now gives such routes a short axis-aligned stub at each misaligned end.
/// </summary>
public class DirectRoutePinEndAlignmentTests
{
    private const double BendRadiusMicrometers = 5.0;

    [Fact]
    public void Route_FacingPinsWithSubRadiusOffset_EndSegmentsRunAlongPinAxes()
    {
        // Facing pins (0° and 180°) with a 0.169 µm lateral offset — the SiEPIC
        // grating-coupler/Y-branch pin heights on a snap-grid placement.
        var start = Pin(x: 59.969, y: 33.669, angleDegrees: 0);
        var end = Pin(x: 110.1, y: 33.5, angleDegrees: 180);

        var path = new WaveguideRouter { MinBendRadiusMicrometers = BendRadiusMicrometers }
            .Route(start, end);

        path.ShouldNotBeNull();
        path.IsValid.ShouldBeTrue();
        path.Segments.Count.ShouldBeGreaterThan(1,
            "the single diagonal must gain axis-aligned stubs at both pins");

        var first = path.Segments.First().ShouldBeOfType<StraightSegment>();
        SegmentAngle(first).ShouldBe(0.0, tolerance: 0.05);
        var last = path.Segments.Last().ShouldBeOfType<StraightSegment>();
        SegmentAngle(last).ShouldBe(0.0, tolerance: 0.05);

        var (sx, sy) = start.GetAbsolutePosition();
        var (ex, ey) = end.GetAbsolutePosition();
        first.StartPoint.X.ShouldBe(sx, tolerance: 1e-6);
        first.StartPoint.Y.ShouldBe(sy, tolerance: 1e-6);
        last.EndPoint.X.ShouldBe(ex, tolerance: 1e-6);
        last.EndPoint.Y.ShouldBe(ey, tolerance: 1e-6);
    }

    [Fact]
    public void Route_ExactlyCoaxialFacingPins_StaysSingleStraight()
    {
        var start = Pin(x: 0, y: 0, angleDegrees: 0);
        var end = Pin(x: 100, y: 0, angleDegrees: 180);

        var path = new WaveguideRouter { MinBendRadiusMicrometers = BendRadiusMicrometers }
            .Route(start, end);

        path.ShouldNotBeNull();
        path.Segments.ShouldHaveSingleItem("coaxial pins keep their exact single straight");
    }

    private static double SegmentAngle(StraightSegment segment) =>
        Math.Atan2(
            segment.EndPoint.Y - segment.StartPoint.Y,
            segment.EndPoint.X - segment.StartPoint.X) * 180.0 / Math.PI;

    private static PhysicalPin Pin(double x, double y, double angleDegrees)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"Block_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0)
        {
            WidthMicrometers = 0,
            HeightMicrometers = 0,
            PhysicalX = x,
            PhysicalY = y
        };
        return new PhysicalPin
        {
            Name = "pin",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = angleDegrees,
            ParentComponent = component
        };
    }
}
