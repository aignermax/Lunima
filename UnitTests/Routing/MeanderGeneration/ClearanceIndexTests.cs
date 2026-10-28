using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using CAP_Core.Routing.MeanderGeneration.Clearance;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.MeanderGeneration;

/// <summary>
/// The spatial index behind the meander clearance checks: replacing a route in place, ignoring
/// owners, and agreeing with the exact sampled-distance verdict.
/// </summary>
public class ClearanceIndexTests
{
    private const double Spacing = 2.0;
    private const double Contact = 1.2;

    private static RoutedPath Straight(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, Math.Atan2(y2 - y1, x2 - x1) * 180.0 / Math.PI));
        return path;
    }

    private static bool Collides(RoutedPath probe, ClearanceIndex index, IReadOnlySet<object>? ignored = null) =>
        MeanderCollisionChecker.Collides(probe,
            MeanderObstacles.FromIndex(index, ignored ?? new HashSet<object>(), Spacing, Contact), 0.0);

    [Fact]
    public void SetRoute_ReplacesTheOwnersPreviousRoute()
    {
        var owner = new object();
        var index = new ClearanceIndex();
        index.SetRoute(owner, Straight(0, 1, 100, 1));
        var probe = Straight(0, 0, 100, 0);
        Collides(probe, index).ShouldBeTrue();

        index.SetRoute(owner, Straight(0, 50, 100, 50));

        Collides(probe, index).ShouldBeFalse();
    }

    [Fact]
    public void IgnoredOwners_AreNotObstacles()
    {
        var route = new object();
        var component = new object();
        var index = new ClearanceIndex();
        index.SetRoute(route, Straight(0, 1, 100, 1));
        index.AddFootprint(new MeanderBounds(40, -10, 60, 10), component);
        var probe = Straight(0, 0, 100, 0);

        Collides(probe, index, new HashSet<object> { route }).ShouldBeTrue("the footprint still blocks");
        Collides(probe, index, new HashSet<object> { route, component }).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(1.9, true)]
    [InlineData(2.1, false)]
    [InlineData(-1.5, true)]
    public void Collides_AgreesWithTheSampledDistance(double offset, bool expected)
    {
        var neighbour = new RoutedPath();
        neighbour.Segments.Add(new BendSegment(50, 30 + offset, 30, 315, 90));
        var probe = Straight(0, 0, 100, 0);
        var index = new ClearanceIndex();
        index.SetRoute(neighbour, neighbour);

        Collides(probe, index).ShouldBe(expected);
        (PathIntersectionDetector.MinimumDistance(probe, neighbour) < Spacing).ShouldBe(expected);
    }

    [Fact]
    public void RemoveRoute_LeavesTheIndexEmpty()
    {
        var owner = new object();
        var index = new ClearanceIndex();
        index.SetRoute(owner, Straight(0, 0, 10, 0));

        index.RemoveRoute(owner);

        index.IsEmpty.ShouldBeTrue();
    }
}
