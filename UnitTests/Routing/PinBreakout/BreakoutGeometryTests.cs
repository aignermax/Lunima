using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.PinBreakout;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.PinBreakout;

/// <summary>
/// Exact breakout geometry: the S-bend's forward-length formula, tangent continuity, radius
/// and end position of S-bends and quarter turns in every heading.
/// </summary>
public class BreakoutGeometryTests
{
    private const double Radius = 10.0;
    private const double Tolerance = 1e-6;

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(4.0, 12.0)]            // h = 4, R = 10: 2·√(100 − 64) = 12
    [InlineData(-4.0, 12.0)]
    [InlineData(20.0, 20.0)]           // h = 2R: two quarter turns
    [InlineData(35.0, 20.0)]           // beyond 2R the forward length stays 2R
    public void ForwardLength_MatchesTheSBendFormula(double lateral, double expected)
    {
        BreakoutSBend.ForwardLength(lateral, Radius).ShouldBe(expected, Tolerance);
    }

    [Theory]
    [InlineData(0.0, 4.0)]
    [InlineData(0.0, -4.0)]
    [InlineData(90.0, 2.0)]
    [InlineData(180.0, -6.0)]
    [InlineData(270.0, 7.5)]
    [InlineData(0.0, 32.0)]
    [InlineData(45.0, 3.0)]
    public void SBend_ShiftsByTheOffset_ArrivesParallel_AndStaysTangent(double heading, double lateral)
    {
        const double x = 100, y = 50;
        double forward = BreakoutSBend.ForwardLength(lateral, Radius) + 4;

        var segments = BreakoutSBend.Build(x, y, heading, lateral, Radius, forward);

        AssertTangentContinuous(segments, x, y, heading);
        var (endX, endY) = segments[^1].EndPoint;
        PinClusterDetector.Lateral(endX - x, endY - y, heading).ShouldBe(lateral, Tolerance);
        PinClusterDetector.Forward(endX - x, endY - y, heading).ShouldBe(forward, Tolerance);
        AngleUtilities.NormalizeAngle(segments[^1].EndAngleDegrees - heading).ShouldBe(0, Tolerance);
        segments.OfType<BendSegment>().ShouldAllBe(b => Math.Abs(b.RadiusMicrometers - Radius) < Tolerance);
    }

    [Fact]
    public void SBend_WithoutOffset_IsOneStraight()
    {
        var segments = BreakoutSBend.Build(0, 0, 0, 0, Radius, 8);

        segments.Count.ShouldBe(1);
        segments[0].ShouldBeOfType<StraightSegment>();
        segments[0].EndPoint.X.ShouldBe(8, Tolerance);
    }

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.0, -1)]
    [InlineData(180.0, 1)]
    [InlineData(90.0, -1)]
    public void QuarterTurn_EndsOneRadiusOutAndSideways_HeadingTheLane(double heading, int side)
    {
        const double x = 10, y = 20, run = 12;

        var segments = BreakoutQuarterTurn.Build(x, y, heading, run, side, Radius);

        AssertTangentContinuous(segments, x, y, heading);
        var (endX, endY) = segments[^1].EndPoint;
        PinClusterDetector.Forward(endX - x, endY - y, heading).ShouldBe(run + Radius, Tolerance);
        PinClusterDetector.Lateral(endX - x, endY - y, heading).ShouldBe(side * Radius, Tolerance);
        AngleUtilities.NormalizeAngle(segments[^1].EndAngleDegrees - (heading + side * 90)).ShouldBe(0, Tolerance);
    }

    /// <summary>Starts at the pin along its heading; every joint continues position and tangent.</summary>
    private static void AssertTangentContinuous(List<PathSegment> segments, double x, double y, double heading)
    {
        segments.ShouldNotBeEmpty();
        segments[0].StartPoint.X.ShouldBe(x, Tolerance);
        segments[0].StartPoint.Y.ShouldBe(y, Tolerance);
        AngleUtilities.NormalizeAngle(segments[0].StartAngleDegrees - heading).ShouldBe(0, Tolerance);
        for (int i = 1; i < segments.Count; i++)
        {
            segments[i].StartPoint.X.ShouldBe(segments[i - 1].EndPoint.X, Tolerance);
            segments[i].StartPoint.Y.ShouldBe(segments[i - 1].EndPoint.Y, Tolerance);
            AngleUtilities.NormalizeAngle(segments[i].StartAngleDegrees - segments[i - 1].EndAngleDegrees)
                .ShouldBe(0, Tolerance);
        }
    }
}
