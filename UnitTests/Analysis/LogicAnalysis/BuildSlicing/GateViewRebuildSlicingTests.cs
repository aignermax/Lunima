using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.GateView;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using UnitTests.Helpers;
using Xunit;
using static UnitTests.Analysis.LogicAnalysis.BuildSlicing.CaptureSlicingFixture;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>
/// The gate view's rebuild captures gate physics in dispatcher frames: a design edit in such a
/// frame stops the rebuild before any gate reaches the classification worker, and an
/// uninterrupted rebuild still publishes every gate.
/// </summary>
public class GateViewRebuildSlicingTests
{
    private const int GateCount = GateViewClassificationSnapshot.NodesPerSlice + 8;

    [AvaloniaFact]
    public async Task EditBetweenCaptureSlices_StopsTheRebuildBeforeClassification()
    {
        await ContinueInsideDispatcherJobAsync();
        await using var shell = ShellWithGates();
        var extractor = new GateViewSnapshotFixture.CountingExtractor();
        var view = CreateView(shell, extractor);
        try
        {
            Dispatcher.UIThread.Post(() => shell.Canvas.Document.Invalidate());

            await view.RebuildNowAsync();

            extractor.Calls.ShouldBe(0, "a capture that saw the design change must never reach the worker");
            view.Graph.ShouldBeSameAs(GateViewGraph.Empty);
            view.IsStale.ShouldBeTrue();
        }
        finally { await view.StopAsync(); }
    }

    [AvaloniaFact]
    public async Task UninterruptedRebuild_PublishesEveryGate()
    {
        await ContinueInsideDispatcherJobAsync();
        await using var shell = ShellWithGates();
        var view = CreateView(shell, new GateViewSnapshotFixture.CountingExtractor());
        try
        {
            await view.RebuildNowAsync();

            view.Graph.Nodes.Count.ShouldBe(GateCount);
            view.IsStale.ShouldBeFalse();
        }
        finally { await view.StopAsync(); }
    }

    private static MainViewModel ShellWithGates()
    {
        var shell = MainViewModelTestHelper.CreateMainViewModel();
        for (var index = 0; index < GateCount; index++)
            shell.Canvas.Components.Add(new ComponentViewModel(LogicSnapshotFixture.Gate($"g{index:D3}")));
        return shell;
    }

    private static GateViewViewModel CreateView(MainViewModel shell, GateViewSnapshotFixture.CountingExtractor extractor) =>
        new(shell.Canvas, shell.WorkspaceSwitch, () => LogicSnapshotFixture.Wavelength,
            classifier: new GateFunctionClassifier(extractor));
}
