using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion;
using Xunit;

namespace UnitTests.Integration;

[Trait("Category", "Slow")]
public class TmpChainPassCalibrationTests
{
    [Fact]
    public async Task Ram4x4_ChainPass_Timing()
    {
        var (canvas, ops, _) = await MziFringeAnalysis.LoadExample("Logic Gate RAM 4x4.lun");
        await ops.PostLoadRouting;
        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action());
        var budgetText = Environment.GetEnvironmentVariable("CALIB_BUDGET");
        if (!string.IsNullOrEmpty(budgetText)) binder.Service.MaxChainPassNodesExpanded = long.Parse(budgetText);
        int blockedBefore = canvas.ConnectionManager.Connections.Count(c => c.IsBlockedFallback);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int connected = binder.Service.ConnectBlockedWiresThroughCrossings(canvas.ConnectionManager, canvas.Router);
        watch.Stop();
        int blockedAfter = canvas.ConnectionManager.Connections.Count(c => c.IsBlockedFallback);
        string label = Environment.GetEnvironmentVariable("CALIB_LABEL") ?? "run";
        File.AppendAllText("/tmp/calib1623/results.txt",
            $"{label}: elapsed={watch.Elapsed.TotalSeconds:F2}s connected={connected} blockedBefore={blockedBefore} blockedAfter={blockedAfter} crossings={canvas.Components.Count(c => c.Component.IsInsertedCrossing)} budget={binder.Service.MaxChainPassNodesExpanded} nodes={binder.Service.LastChainPassNodesExpanded}\n");
    }
}
