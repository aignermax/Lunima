using System.Diagnostics;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Perf guard for the coarse-grid retry (issue #1426, follow-up to #1418/#1423). Pins
/// two behaviours the measurement (<see cref="CoarseRetryCostMeasurementTests"/>) justified:
/// <list type="bullet">
/// <item>A wire whose coarse retry fails (sealed end pin — no physical route) burns at
/// most <see cref="WaveguideRouter.CoarseRetryBudgetMultiplier"/> ×
/// <see cref="WaveguideRouter.Phase2MaxNodes"/> coarse node expansions per
/// <see cref="WaveguideRouter.Route"/> call. Asserted on node expansions, not wall time,
/// so the guard is deterministic.</item>
/// <item>A cancellation requested while the coarse retry is running stops
/// <see cref="WaveguideRouter.Route"/> within one second — Peter's interactive re-route
/// must stay cancellable.</item>
/// </list>
/// </summary>
public class CoarseRetryPerfGuardTests
{
    /// <summary>
    /// Routes the contention scene with the trunk's end pin sealed so the coarse retry
    /// must fail, and asserts the retry stays inside its per-wire budget. Baseline for
    /// "fixed, named per-wire overhead" is the disabled-retry run — it burns zero coarse
    /// expansions by construction, so the entire default-run total is overhead the bound
    /// must cap. The budget itself is the named constant
    /// <see cref="WaveguideRouter.CoarseRetryBudgetMultiplier"/> on the router.
    /// </summary>
    [Fact]
    public void SealedTrunk_CoarseRetry_StaysWithinPerWireBudget()
    {
        var scene = ContentionRepairDetourReproTests.SelectTrunkScene.BuildSealedTrunk();
        long budgetPerRoute = (long)scene.Router.CoarseRetryBudgetMultiplier * scene.Router.Phase2MaxNodes;

        var perRouteExpansions = new List<long>();
        scene.Router.OnRouteCoarseExpansionsRecorded = perRouteExpansions.Add;

        scene.Manager.RecalculateAllTransmissions();

        scene.Trunk.RoutedPath.ShouldNotBeNull();
        scene.Trunk.IsBlockedFallback.ShouldBeTrue(
            "the sealed trunk has no physical route — the retry must fail");
        perRouteExpansions.ShouldNotBeEmpty(
            "the retry ran at least once for the contended trunk");
        foreach (var spent in perRouteExpansions)
        {
            spent.ShouldBeLessThanOrEqualTo(budgetPerRoute,
                $"a single Route() call burned {spent} coarse expansions — over the " +
                $"per-wire budget of {budgetPerRoute} " +
                $"({scene.Router.CoarseRetryBudgetMultiplier}× Phase2MaxNodes)");
        }
    }

    /// <summary>
    /// A cancellation requested while the coarse retry is running (hooked via
    /// <see cref="WaveguideRouter.OnCoarseRetryStarted"/> so the cancel lands inside
    /// the retry, not before it) must stop <see cref="WaveguideRouter.Route"/> within
    /// one second. The A* checks the token every 500 expansions; without an honoured
    /// token a flooded coarse retry would run for many seconds before the Manhattan
    /// fallback returns.
    /// </summary>
    [Fact]
    public void Cancellation_DuringCoarseRetry_ReturnsWithinOneSecond()
    {
        // The unsealed contention scene: the trunk's fine A* floods the free plane
        // and exhausts its budget, so the coarse retry engages. The cancel is hooked
        // to the retry's entry so the measurement covers only the cancelled-retry
        // teardown, not the fine flood that preceded it.
        var scene = ContentionRepairDetourReproTests.SelectTrunkScene.Build();
        using var cts = new CancellationTokenSource();
        var sinceCancel = new Stopwatch();
        bool cancelRequested = false;
        scene.Router.OnCoarseRetryStarted = () =>
        {
            cts.Cancel();
            cancelRequested = true;
            sinceCancel.Start();
        };

        var path = scene.Router.Route(scene.TrunkStart, scene.TrunkEnd, cts.Token);
        sinceCancel.Stop();

        path.ShouldNotBeNull();
        cancelRequested.ShouldBeTrue(
            "the test setup must drive Route() into the coarse retry — otherwise the " +
            "assertion measures nothing");
        sinceCancel.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1),
            $"Route() took {sinceCancel.Elapsed.TotalMilliseconds:F0}ms after the coarse " +
            "retry observed the cancellation — the token must stop the search, not let " +
            "it flood");
    }
}
