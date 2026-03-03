using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Routing;

/// <summary>
/// Tests that routing produces paths whose endpoints exactly match pin positions,
/// and that consecutive segments chain without gaps.
/// These tests catch the GDS export gap issue where "snap" segments go diagonal
/// but Nazca interprets them as forward-only.
/// </summary>
public class RoutingEndpointAccuracyTests
{
    private readonly ITestOutputHelper _output;
    private const double ToleranceMicrometers = 0.1;

    public RoutingEndpointAccuracyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Route_StraightAlignment_EndpointsMatchPins()
    {
        var (startPin, endPin) = CreateAlignedPins(0, 25, 100, 25);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        AssertEndpointsMatchPins(path, startPin, endPin);
    }

    [Fact]
    public void Route_ParallelOffset_EndpointsMatchPins()
    {
        // Start pin at (50, 25) heading East, end pin at (200, 75) heading West
        var (startPin, endPin) = CreateOffsetPins(
            startCompX: 0, startCompY: 0, startPinOffsetX: 50, startPinOffsetY: 25, startAngle: 0,
            endCompX: 200, endCompY: 50, endPinOffsetX: 0, endPinOffsetY: 25, endAngle: 180);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        DumpSegments(path, startPin, endPin);
        AssertEndpointsMatchPins(path, startPin, endPin);
    }

    [Fact]
    public void Route_ParallelOffset_SegmentsChainWithoutGaps()
    {
        var (startPin, endPin) = CreateOffsetPins(
            startCompX: 0, startCompY: 0, startPinOffsetX: 50, startPinOffsetY: 25, startAngle: 0,
            endCompX: 200, endCompY: 50, endPinOffsetX: 0, endPinOffsetY: 25, endAngle: 180);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        AssertSegmentsContinuous(path);
    }

    [Fact]
    public void Route_ParallelOffset_StraightSegmentsAreDirectionAligned()
    {
        var (startPin, endPin) = CreateOffsetPins(
            startCompX: 0, startCompY: 0, startPinOffsetX: 50, startPinOffsetY: 25, startAngle: 0,
            endCompX: 200, endCompY: 50, endPinOffsetX: 0, endPinOffsetY: 25, endAngle: 180);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        DumpSegments(path, startPin, endPin);
        AssertStraightSegmentsDirectionAligned(path);
    }

    [Fact]
    public void Route_PerpendicularPins_EndpointsMatchPins()
    {
        var (startPin, endPin) = CreateOffsetPins(
            startCompX: 0, startCompY: 0, startPinOffsetX: 25, startPinOffsetY: 50, startAngle: 90,
            endCompX: 50, endCompY: 50, endPinOffsetX: 0, endPinOffsetY: 25, endAngle: 180);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        DumpSegments(path, startPin, endPin);
        AssertEndpointsMatchPins(path, startPin, endPin);
    }

    [Fact]
    public void Route_PerpendicularPins_StraightSegmentsAreDirectionAligned()
    {
        var (startPin, endPin) = CreateOffsetPins(
            startCompX: 0, startCompY: 0, startPinOffsetX: 25, startPinOffsetY: 50, startAngle: 90,
            endCompX: 50, endCompY: 50, endPinOffsetX: 0, endPinOffsetY: 25, endAngle: 180);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        AssertStraightSegmentsDirectionAligned(path);
    }

    [Theory]
    [InlineData(10)]  // Small offset
    [InlineData(25)]  // Medium offset
    [InlineData(50)]  // Large offset (> bend radius)
    public void Route_VariousOffsets_EndpointsMatchPins(double yOffset)
    {
        var (startPin, endPin) = CreateOffsetPins(
            startCompX: 0, startCompY: 0, startPinOffsetX: 50, startPinOffsetY: 25, startAngle: 0,
            endCompX: 150, endCompY: yOffset, endPinOffsetX: 0, endPinOffsetY: 25, endAngle: 180);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        DumpSegments(path, startPin, endPin);
        AssertEndpointsMatchPins(path, startPin, endPin);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public void Route_VariousOffsets_StraightSegmentsDirectionAligned(double yOffset)
    {
        var (startPin, endPin) = CreateOffsetPins(
            startCompX: 0, startCompY: 0, startPinOffsetX: 50, startPinOffsetY: 25, startAngle: 0,
            endCompX: 150, endCompY: yOffset, endPinOffsetX: 0, endPinOffsetY: 25, endAngle: 180);
        var router = CreateRouter();

        var path = router.Route(startPin, endPin);

        path.ShouldNotBeNull();
        DumpSegments(path, startPin, endPin);
        AssertStraightSegmentsDirectionAligned(path);
    }

    /// <summary>
    /// Verifies that first segment starts at start pin and last segment ends at end pin.
    /// </summary>
    private void AssertEndpointsMatchPins(RoutedPath path, PhysicalPin startPin, PhysicalPin endPin)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        var firstSeg = path.Segments[0];
        var lastSeg = path.Segments[^1];

        var startDist = Distance(firstSeg.StartPoint, (startX, startY));
        var endDist = Distance(lastSeg.EndPoint, (endX, endY));

        startDist.ShouldBeLessThan(ToleranceMicrometers,
            $"First segment starts at ({firstSeg.StartPoint.X:F3}, {firstSeg.StartPoint.Y:F3}), " +
            $"but start pin is at ({startX:F3}, {startY:F3}). Gap: {startDist:F3}µm");

        endDist.ShouldBeLessThan(ToleranceMicrometers,
            $"Last segment ends at ({lastSeg.EndPoint.X:F3}, {lastSeg.EndPoint.Y:F3}), " +
            $"but end pin is at ({endX:F3}, {endY:F3}). Gap: {endDist:F3}µm");
    }

    /// <summary>
    /// Verifies consecutive segments chain without gaps.
    /// </summary>
    private void AssertSegmentsContinuous(RoutedPath path)
    {
        for (int i = 1; i < path.Segments.Count; i++)
        {
            var prev = path.Segments[i - 1];
            var curr = path.Segments[i];
            var gap = Distance(prev.EndPoint, curr.StartPoint);

            gap.ShouldBeLessThan(ToleranceMicrometers,
                $"Gap between segment {i - 1} end ({prev.EndPoint.X:F3}, {prev.EndPoint.Y:F3}) " +
                $"and segment {i} start ({curr.StartPoint.X:F3}, {curr.StartPoint.Y:F3}): {gap:F3}µm");
        }
    }

    /// <summary>
    /// Verifies that straight segments actually go in the direction of their stored angle.
    /// This is critical for Nazca export: nd.strt(length=L).put() goes forward along
    /// the propagation direction, so if the segment's direction doesn't match its angle,
    /// the Nazca path diverges from the intended path.
    /// </summary>
    private void AssertStraightSegmentsDirectionAligned(RoutedPath path)
    {
        for (int i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i] is not StraightSegment straight)
                continue;

            double dx = straight.EndPoint.X - straight.StartPoint.X;
            double dy = straight.EndPoint.Y - straight.StartPoint.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.01)
                continue; // Skip zero-length segments

            // Actual direction from start to end
            double actualAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
            // Stored propagation angle
            double storedAngle = straight.StartAngleDegrees;

            // Normalize both to 0-360
            actualAngle = ((actualAngle % 360) + 360) % 360;
            storedAngle = ((storedAngle % 360) + 360) % 360;

            double angleDiff = Math.Abs(actualAngle - storedAngle);
            if (angleDiff > 180) angleDiff = 360 - angleDiff;

            // Project length onto propagation direction to find lateral offset
            double angleRad = straight.StartAngleDegrees * Math.PI / 180;
            double forwardLen = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
            double lateralLen = Math.Abs(-dx * Math.Sin(angleRad) + dy * Math.Cos(angleRad));

            if (angleDiff > 2.0)
            {
                _output.WriteLine(
                    $"MISALIGNED segment {i}: angle={storedAngle:F1}° actual={actualAngle:F1}° " +
                    $"diff={angleDiff:F1}° length={length:F2}µm " +
                    $"forward={forwardLen:F2}µm lateral={lateralLen:F2}µm " +
                    $"({straight.StartPoint.X:F2},{straight.StartPoint.Y:F2})→" +
                    $"({straight.EndPoint.X:F2},{straight.EndPoint.Y:F2})");
            }

            angleDiff.ShouldBeLessThan(5.0,
                $"Segment {i}: stored angle={storedAngle:F1}° but actual direction={actualAngle:F1}° " +
                $"(diff={angleDiff:F1}°). Lateral offset={lateralLen:F2}µm over {length:F2}µm. " +
                $"This will cause a {lateralLen:F2}µm gap in Nazca GDS export.");
        }
    }

    private void DumpSegments(RoutedPath path, PhysicalPin startPin, PhysicalPin endPin)
    {
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();

        _output.WriteLine($"Start pin: ({sx:F2}, {sy:F2}) angle={startPin.GetAbsoluteAngle():F0}°");
        _output.WriteLine($"End pin:   ({ex:F2}, {ey:F2}) angle={endPin.GetAbsoluteAngle():F0}°");
        _output.WriteLine($"Segments: {path.Segments.Count}");

        for (int i = 0; i < path.Segments.Count; i++)
        {
            var seg = path.Segments[i];
            if (seg is StraightSegment s)
            {
                _output.WriteLine(
                    $"  [{i}] Straight: ({s.StartPoint.X:F2},{s.StartPoint.Y:F2})→" +
                    $"({s.EndPoint.X:F2},{s.EndPoint.Y:F2}) angle={s.StartAngleDegrees:F1}° len={s.LengthMicrometers:F2}µm");
            }
            else if (seg is BendSegment b)
            {
                _output.WriteLine(
                    $"  [{i}] Bend: ({b.StartPoint.X:F2},{b.StartPoint.Y:F2})→" +
                    $"({b.EndPoint.X:F2},{b.EndPoint.Y:F2}) " +
                    $"r={b.RadiusMicrometers:F1} sweep={b.SweepAngleDegrees:F1}° " +
                    $"angle={b.StartAngleDegrees:F1}°→{b.EndAngleDegrees:F1}°");
            }
        }

        var lastSeg = path.Segments[^1];
        _output.WriteLine($"Last endpoint: ({lastSeg.EndPoint.X:F2}, {lastSeg.EndPoint.Y:F2})");
        _output.WriteLine($"Distance to end pin: {Distance(lastSeg.EndPoint, (ex, ey)):F3}µm");
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static (PhysicalPin start, PhysicalPin end) CreateAlignedPins(
        double startX, double startY, double endX, double endY)
    {
        var startComp = CreateTestComponent(startX - 25, startY - 25);
        var endComp = CreateTestComponent(endX, endY - 25);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 25 + startX - (startX - 25),
            OffsetYMicrometers = 25,
            AngleDegrees = 0,
            ParentComponent = startComp
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180,
            ParentComponent = endComp
        };

        return (startPin, endPin);
    }

    private static (PhysicalPin start, PhysicalPin end) CreateOffsetPins(
        double startCompX, double startCompY, double startPinOffsetX, double startPinOffsetY, double startAngle,
        double endCompX, double endCompY, double endPinOffsetX, double endPinOffsetY, double endAngle)
    {
        var startComp = CreateTestComponent(startCompX, startCompY);
        var endComp = CreateTestComponent(endCompX, endCompY);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = startPinOffsetX,
            OffsetYMicrometers = startPinOffsetY,
            AngleDegrees = startAngle,
            ParentComponent = startComp
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = endPinOffsetX,
            OffsetYMicrometers = endPinOffsetY,
            AngleDegrees = endAngle,
            ParentComponent = endComp
        };

        return (startPin, endPin);
    }

    private static WaveguideRouter CreateRouter() => new()
    {
        MinBendRadiusMicrometers = 10.0,
        MinWaveguideSpacingMicrometers = 2.0
    };

    private static Component CreateTestComponent(double x, double y)
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
            identifier: $"Test_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
