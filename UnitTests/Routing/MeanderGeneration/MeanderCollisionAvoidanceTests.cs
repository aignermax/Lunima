using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.MeanderGeneration;

/// <summary>
/// Collision-aware meander generation: the fold keeps clear of other routes and of component
/// footprints, falls back to the connection's existing route when the compact pin-to-pin route
/// is blocked, honors the requested bend radius, and reports a typed collision failure instead
/// of silently overlapping.
/// </summary>
public class MeanderCollisionAvoidanceTests
{
    private const double Spacing = 2.0;
    private const double Contact = 1.2;
    private const double Tolerance = 0.5;

    private static MeanderRequest Request(double target, MeanderObstacles? obstacles, double halfHeight = 40,
        double radius = 5.0, RoutedPath? basePath = null)
        => new(0, 0, 0, 100, 0, 0, target, Tolerance, radius,
            new MeanderBounds(-10, -halfHeight, 110, halfHeight), obstacles, basePath);

    private static RoutedPath Straight(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, Math.Atan2(y2 - y1, x2 - x1) * 180.0 / Math.PI));
        return path;
    }

    private static MeanderObstacles Routes(params RoutedPath[] routes) =>
        new(routes, Array.Empty<MeanderBounds>(), Spacing, Contact);

    [Fact]
    public void Generate_RouteAlongPreferredSide_FoldsToTheOtherSide()
    {
        var neighbor = Straight(0, 15, 100, 15);

        var result = new MeanderPathGenerator().Generate(Request(250, Routes(neighbor)));

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        result.Path!.TotalLengthMicrometers.ShouldBe(250, Tolerance);
        PathIntersectionDetector.Crosses(result.Path, neighbor).ShouldBeFalse();
        PathIntersectionDetector.MinimumDistance(result.Path, neighbor).ShouldBeGreaterThanOrEqualTo(Spacing);
    }

    [Fact]
    public void Generate_WithoutObstacles_KeepsTheClassicGeometry()
    {
        var unobstructed = new MeanderPathGenerator().Generate(Request(250, null)).Path!;
        var farAway = new MeanderPathGenerator().Generate(Request(250, Routes(Straight(0, 500, 100, 500)))).Path!;

        farAway.Segments.Count.ShouldBe(unobstructed.Segments.Count);
        for (int i = 0; i < unobstructed.Segments.Count; i++)
        {
            farAway.Segments[i].EndPoint.X.ShouldBe(unobstructed.Segments[i].EndPoint.X, 1e-9);
            farAway.Segments[i].EndPoint.Y.ShouldBe(unobstructed.Segments[i].EndPoint.Y, 1e-9);
        }
    }

    [Fact]
    public void Generate_RoutesCloseOnBothSides_FailsWithCollisionInsteadOfOverlapping()
    {
        var above = Straight(0, 8, 100, 8);
        var below = Straight(0, -8, 100, -8);

        var result = new MeanderPathGenerator().Generate(Request(250, Routes(above, below)));

        result.IsSuccess.ShouldBeFalse();
        result.Path.ShouldBeNull();
        result.FailureReason.ShouldBe(MeanderFailureReason.CollidesWithRoutes);
    }

    [Fact]
    public void Generate_ComponentFootprintOnOneSide_FoldsAwayFromIt()
    {
        var footprint = new MeanderBounds(10, 2, 90, 30);
        var obstacles = new MeanderObstacles(Array.Empty<RoutedPath>(), new[] { footprint }, Spacing, Contact);

        var result = new MeanderPathGenerator().Generate(Request(250, obstacles));

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        PathIntersectionDetector.IntersectsRectangle(result.Path!, 10, 2, 90, 30).ShouldBeFalse();
    }

    [Fact]
    public void Generate_CompactRouteBlocked_FoldsIntoTheExistingDetour()
    {
        // A block on the direct line that no fold of the straight pin-to-pin route can clear.
        var block = new MeanderBounds(40, -30, 60, 15);
        var obstacles = new MeanderObstacles(Array.Empty<RoutedPath>(), new[] { block }, Spacing, Contact);
        var detour = Detour();

        var result = new MeanderPathGenerator().Generate(
            Request(250, obstacles, halfHeight: 60, basePath: detour));

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        result.Path!.TotalLengthMicrometers.ShouldBe(250, Tolerance);
        PathIntersectionDetector.IntersectsRectangle(result.Path, 40, -30, 60, 15).ShouldBeFalse();
        result.Path.Segments[1].ShouldBeSameAs(detour.Segments[1], "the detour's own bends are kept");
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(8.0)]
    public void Generate_RequestedRadius_IsUsedByEveryBend(double radius)
    {
        var result = new MeanderPathGenerator().Generate(Request(250, null, radius: radius));

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        result.Path!.TotalLengthMicrometers.ShouldBe(250, Tolerance);
        foreach (var bend in result.Path.Segments.OfType<BendSegment>())
            bend.RadiusMicrometers.ShouldBe(radius, 1e-9);
    }

    [Fact]
    public void Generate_WithObstacles_IsDeterministic()
    {
        var request = Request(250, Routes(Straight(0, 15, 100, 15)));

        var first = new MeanderPathGenerator().Generate(request).Path!;
        var second = new MeanderPathGenerator().Generate(request).Path!;

        second.Segments.Count.ShouldBe(first.Segments.Count);
        second.TotalLengthMicrometers.ShouldBe(first.TotalLengthMicrometers, 1e-12);
    }

    /// <summary>
    /// (0,0) → (100,0) via an up-and-over detour at y = 20 with radius-5 bends: the kind of
    /// route the A* router produces around something on the direct line.
    /// </summary>
    private static RoutedPath Detour()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 10, 0, 0));
        path.Segments.Add(new BendSegment(10, 5, 5, 0, 90));
        path.Segments.Add(new StraightSegment(15, 5, 15, 15, 90));
        path.Segments.Add(new BendSegment(20, 15, 5, 90, -90));
        path.Segments.Add(new StraightSegment(20, 20, 80, 20, 0));
        path.Segments.Add(new BendSegment(80, 15, 5, 0, -90));
        path.Segments.Add(new StraightSegment(85, 15, 85, 5, 270));
        path.Segments.Add(new BendSegment(90, 5, 5, 270, 90));
        path.Segments.Add(new StraightSegment(90, 0, 100, 0, 0));
        return path;
    }
}
