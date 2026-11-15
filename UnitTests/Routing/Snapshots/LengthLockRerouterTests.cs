using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;

namespace UnitTests.Routing.Snapshots;

/// <summary>
/// The layout Auto falls back to when a locked bundle's arches cannot reach the lock: every wire
/// routed afresh by the general router on a private copy of the design, then meandered to the
/// lock — without touching the live design.
/// </summary>
public class LengthLockRerouterTests
{
    private const int ChannelCount = 6;
    /// <summary>
    /// Enough channels for vertical offsets where 250 µm bends turn the router's short diagonal
    /// routes into long axis-parallel ones that no meander can bring back down to the lock.
    /// </summary>
    private const int WideFanInChannelCount = 12;
    private const double Tolerance = 0.1;
    private const double MaxRadius = 250.0;
    private const double MinRadius = 25.0;
    /// <summary>The largest radius "Equalize lengths" stamps on a locked route as its meander radius.</summary>
    private const double StampedMeanderRadius = 250.0;
    /// <summary>Far below the pin-to-pin distance of every fan-in channel.</summary>
    private const double UnreachableLockMicrometers = 100.0;

    [Fact]
    public async Task Solve_LockAtTheLongestRoute_IgnoresTheStampedMeanderRadius_AndMeandersEveryWireOntoIt()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(WideFanInChannelCount);
        var models = connections.Select(c => c.Connection).ToList();
        double target = models.Max(c => c.PathLengthMicrometers);
        foreach (var model in models)
            model.BendRadiusMicrometers = StampedMeanderRadius;
        var live = models.ToDictionary(c => c.Id, c => c.RoutedPath);

        var result = LengthLockRerouter.Solve(Capture(canvas), live.Keys, target, Tolerance, MaxRadius, MinRadius);

        result.AllMatched.ShouldBeTrue($"{result.Matched.Count}/{result.WireCount} on the lock");
        result.Missed.ShouldBeEmpty();
        foreach (var path in result.Matched.Values)
            path.TotalLengthMicrometers.ShouldBe(target, Tolerance);
        AssertNoCrossings(result.Matched.Values.ToList());
        models.ShouldAllBe(c => ReferenceEquals(c.RoutedPath, live[c.Id]), "the live routes stay untouched");
        models.ShouldAllBe(c => c.BendRadiusMicrometers == StampedMeanderRadius, "the live radii stay untouched");
    }

    [Fact]
    public async Task Solve_LockShorterThanEveryRoute_MatchesNone_AndReturnsTheRoutedPathsAsMissed()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(ChannelCount);
        var ids = connections.Select(c => c.Connection.Id).ToList();

        var result = LengthLockRerouter.Solve(Capture(canvas), ids, UnreachableLockMicrometers, Tolerance, MaxRadius, MinRadius);

        result.AllMatched.ShouldBeFalse();
        result.Matched.ShouldBeEmpty();
        result.Missed.Keys.ShouldBe(ids, ignoreOrder: true);
        result.Missed.Values.ShouldAllBe(p => p.TotalLengthMicrometers > UnreachableLockMicrometers);
    }

    [Fact]
    public async Task Solve_WithNoRequestedWireInTheDesign_IsNotAMatch()
    {
        var (canvas, _) = await FanInDesign.CreateAsync(ChannelCount);

        var result = LengthLockRerouter.Solve(Capture(canvas), new[] { Guid.NewGuid() }, 1000, Tolerance, MaxRadius, MinRadius);

        result.WireCount.ShouldBe(0);
        result.AllMatched.ShouldBeFalse();
    }

    private static RoutingInputSnapshot Capture(DesignCanvasViewModel canvas) =>
        RoutingInputSnapshot.Capture(canvas.Components.Select(c => c.Component).ToArray(), canvas.Router,
            canvas.ConnectionManager, preserveCachedGrid: false);

    private static void AssertNoCrossings(IReadOnlyList<RoutedPath> paths)
    {
        for (int i = 0; i < paths.Count; i++)
            for (int j = i + 1; j < paths.Count; j++)
                PathIntersectionDetector.Crosses(paths[i], paths[j]).ShouldBeFalse($"routes {i} and {j} cross");
    }
}
