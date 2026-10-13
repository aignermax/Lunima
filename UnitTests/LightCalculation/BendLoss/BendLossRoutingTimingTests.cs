using System.Diagnostics;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.LightCalculation.BendLoss;

/// <summary>
/// Measures a full routing recalculation of the shipped 2-bit counter example (every
/// top-level route invalidated, then re-routed through the real orchestrator, including
/// the post-routing bend-upsizing pass). Reports the median of a few runs so the
/// loss-aware upsizer's cost can be compared against the previous constant-loss router.
/// Slow: loads and routes a full example, so it only runs on CI.
/// </summary>
[Trait("Category", "Slow")]
public class BendLossRoutingTimingTests
{
    private const string ExampleFileName = "Logic Gate Counter 2-bit.lun";
    private const int Runs = 3;

    /// <summary>Generous ceiling that only catches pathological slowdowns (runner speed varies widely).</summary>
    private const double MaxMedianMilliseconds = 90_000;

    private readonly ITestOutputHelper _output;

    /// <summary>Captures the test output for the timing report.</summary>
    public BendLossRoutingTimingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Counter2Bit_FullRecalculation_StaysFast()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        var timings = new List<double>();
        for (int run = 0; run < Runs; run++)
        {
            foreach (var connection in canvas.ConnectionManager.Connections.ToList())
            {
                if (!connection.IsRouteFrozen)
                    connection.InvalidateRoute();
            }

            var stopwatch = Stopwatch.StartNew();
            await canvas.RecalculateRoutesAsync();
            stopwatch.Stop();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        double median = timings[timings.Count / 2];
        _output.WriteLine(
            $"Counter 2-bit full recalculation: median {median:F0} ms over {Runs} runs " +
            $"({string.Join(", ", timings.Select(t => t.ToString("F0")))} ms)");
        median.ShouldBeLessThan(MaxMedianMilliseconds);
    }
}
