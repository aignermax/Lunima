using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Measurement for the coarse-grid retry cost (issue #1426, follow-up to #1418/#1423).
/// Routes the <see cref="ContentionRepairDetourReproTests.SelectTrunkScene"/> word-cell
/// fixture — plus a variant whose trunk end pin is sealed inside its gate body so the
/// retry must fail — with the retry disabled (<see cref="WaveguideRouter.CoarseRetryCellSizeFactor"/>
/// = 1) and with the default factor, and logs the wall time and coarse node expansions per
/// blocked wire. The numbers justify the cap <see cref="WaveguideRouter.CoarseRetryBudgetMultiplier"/>
/// ships with; this test only asserts the whole run stays inside the Slow-test budget.
/// </summary>
[Trait("Category", "Slow")]
public class CoarseRetryCostMeasurementTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>CI ceiling for one Slow test (issue #1426).</summary>
    private static readonly TimeSpan SlowTestWallBudget = TimeSpan.FromSeconds(60);

    public CoarseRetryCostMeasurementTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CoarseRetry_PerBlockedWireCost_IsMeasured()
    {
        var total = System.Diagnostics.Stopwatch.StartNew();

        var freeBase = Run("free-highway, retry disabled", sealTrunkEnd: false, coarseFactor: 1);
        var freeRetry = Run("free-highway, retry default", sealTrunkEnd: false, coarseFactor: 4);
        var sealedBase = Run("sealed trunk, retry disabled", sealTrunkEnd: true, coarseFactor: 1);
        var sealedRetry = Run("sealed trunk, retry default", sealTrunkEnd: true, coarseFactor: 4);

        _output.WriteLine(string.Empty);
        _output.WriteLine("=== Coarse retry cost per blocked wire (issue #1426) ===");
        Report(freeBase);
        Report(freeRetry);
        Report(sealedBase);
        Report(sealedRetry);

        total.Stop();
        _output.WriteLine($"Total measurement wall time: {total.Elapsed.TotalSeconds:F1}s " +
                          $"(budget {SlowTestWallBudget.TotalSeconds:F0}s)");
        total.Elapsed.ShouldBeLessThan(SlowTestWallBudget,
            "the measurement must fit the Slow-test CI budget");
    }

    private void Report(Measurement m)
    {
        _output.WriteLine(
            $"[{m.Label}] wall {m.Wall.TotalSeconds,6:F2}s | wires {m.WireCount} | " +
            $"blocked {m.BlockedCount} | coarse attempts {m.CoarseAttemptedWires} | " +
            $"coarse expansions total {m.TotalCoarseExpansions} | " +
            $"per blocked wire {m.CoarseExpansionsPerBlockedWire}");
    }

    private Measurement Run(string label, bool sealTrunkEnd, int coarseFactor)
    {
        var scene = sealTrunkEnd
            ? ContentionRepairDetourReproTests.SelectTrunkScene.BuildSealedTrunk()
            : ContentionRepairDetourReproTests.SelectTrunkScene.Build();
        scene.Router.CoarseRetryCellSizeFactor = coarseFactor;

        var perWireExpansions = new List<long>();
        scene.Router.OnRouteCoarseExpansionsRecorded = perWireExpansions.Add;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        scene.Manager.RecalculateAllTransmissions();
        watch.Stop();

        long totalCoarse = perWireExpansions.Sum();
        int coarseAttempted = perWireExpansions.Count(e => e > 0);
        var allWires = scene.Siblings.Concat(new[] { scene.Trunk }).ToList();
        int blocked = allWires.Count(w => w.RoutedPath?.IsBlockedFallback == true);
        long perBlocked = blocked > 0 ? totalCoarse / blocked : 0;

        return new Measurement(label, watch.Elapsed, allWires.Count, blocked,
                               coarseAttempted, totalCoarse, perBlocked);
    }

    private sealed record Measurement(
        string Label, TimeSpan Wall, int WireCount, int BlockedCount,
        int CoarseAttemptedWires, long TotalCoarseExpansions, long CoarseExpansionsPerBlockedWire);
}
