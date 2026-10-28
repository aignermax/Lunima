using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.MeanderGeneration;

/// <summary>
/// A route boxed in by parallel neighbours on both sides still gets a little extra length when
/// one neighbour already carries a meander: the fold nests inside the neighbour's loop, the only
/// free spot along the route — not at the center, start or end where a fixed placement looked.
/// Also: an existing route with tighter bends than the meander radius can still host a fold.
/// </summary>
public class MeanderNestingTests
{
    private const double Spacing = 2.0;
    private const double Contact = 1.2;
    private const double Tolerance = 0.1;
    private const double Radius = 10.0;
    private const double Pitch = 20.0;
    private const double NeighbourLoopStart = 600.0;
    private const double NeighbourLoopWidth = 80.0;
    private const double NeighbourLoopHeight = 200.0;

    [Fact]
    public void Generate_NeighbourWithLoop_NestsTheFoldInsideIt()
    {
        var above = NeighbourWithLoop();
        var below = Straight(0, -Pitch, 1000, -Pitch);
        var obstacles = new MeanderObstacles([above, below], [], Spacing, Contact);
        var request = new MeanderRequest(0, 0, 0, 1000, 0, 0, 1080, Tolerance, Radius,
            new MeanderBounds(-10, -400, 1010, 400), obstacles);

        var result = new MeanderPathGenerator().Generate(request);

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        result.Path!.TotalLengthMicrometers.ShouldBe(1080, Tolerance);
        PathIntersectionDetector.MinimumDistance(result.Path, above).ShouldBeGreaterThanOrEqualTo(Spacing - 1e-6);
        PathIntersectionDetector.MinimumDistance(result.Path, below).ShouldBeGreaterThanOrEqualTo(Spacing - 1e-6);
        var foldXs = result.Path.Segments.OfType<BendSegment>().Select(b => b.Center.X).ToList();
        foldXs.ShouldAllBe(x => x > NeighbourLoopStart && x < NeighbourLoopStart + NeighbourLoopWidth,
            "the only room is inside the neighbour's loop");
    }

    [Fact]
    public void Generate_ExistingRouteWithTighterBends_StillHostsALargerRadiusFold()
    {
        var existing = new RoutedPath();
        existing.Segments.Add(new StraightSegment(0, 0, 300, 0, 0));
        existing.Segments.Add(new BendSegment(300, 2, 2, 0, 90));
        existing.Segments.Add(new StraightSegment(302, 2, 302, 300, 90));
        var blockOnDirectRoute = new MeanderBounds(100, 20, 280, 400);
        var obstacles = new MeanderObstacles([], [blockOnDirectRoute], Spacing, Contact);
        double target = existing.TotalLengthMicrometers + 120;
        var request = new MeanderRequest(0, 0, 0, 302, 300, 90, target, Tolerance, MinBendRadiusMicrometers: 5.0,
            new MeanderBounds(-10, -400, 700, 700), obstacles, existing, MeanderRadiusMicrometers: Radius,
            PreferExistingRoute: true);

        var result = new MeanderPathGenerator().Generate(request);

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        result.Path!.TotalLengthMicrometers.ShouldBe(target, Tolerance);
        result.Path.Segments.ShouldContain(s => ReferenceEquals(s, existing.Segments[1]), "the existing route is kept");
    }

    private static RoutedPath Straight(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, Math.Atan2(y2 - y1, x2 - x1) * 180.0 / Math.PI));
        return path;
    }

    /// <summary>
    /// The neighbour above (y = +20) leaves its line at x = 600, rises 200 µm, crosses 80 µm and
    /// comes back — a loop on the far side, open toward the matched route.
    /// </summary>
    private static RoutedPath NeighbourWithLoop()
    {
        double y = Pitch;
        double x0 = NeighbourLoopStart;
        double x1 = NeighbourLoopStart + NeighbourLoopWidth;
        double top = y + NeighbourLoopHeight;
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, y, x0 - Radius, y, 0));
        path.Segments.Add(new BendSegment(x0 - Radius, y + Radius, Radius, 0, 90));
        path.Segments.Add(new StraightSegment(x0, y + Radius, x0, top - Radius, 90));
        path.Segments.Add(new BendSegment(x0 + Radius, top - Radius, Radius, 90, -90));
        path.Segments.Add(new StraightSegment(x0 + Radius, top, x1 - Radius, top, 0));
        path.Segments.Add(new BendSegment(x1 - Radius, top - Radius, Radius, 0, -90));
        path.Segments.Add(new StraightSegment(x1, top - Radius, x1, y + Radius, 270));
        path.Segments.Add(new BendSegment(x1 + Radius, y + Radius, Radius, 270, 90));
        path.Segments.Add(new StraightSegment(x1 + Radius, y, 1000, y, 0));
        return path;
    }
}
