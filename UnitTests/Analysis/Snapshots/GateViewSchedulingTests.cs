using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.GateView;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Snapshots;

public sealed class GateViewSchedulingTests
{
    [AvaloniaFact]
    public async Task WorkspaceEntryAndImmediateRequest_CoalesceBeforeOwnerCapture_ThenClassifyOffThread()
    {
        await using var shell = MainViewModelTestHelper.CreateMainViewModel();
        await shell.GateView.StopAsync();
        var gate = LogicSnapshotFixture.Gate("scheduled");
        shell.Canvas.Components.Add(new ComponentViewModel(gate));
        var captures = 0;
        var captureOnOwner = false;
        var extractor = new HeldThreadRecordingExtractor();
        var view = new GateViewViewModel(shell.Canvas, shell.WorkspaceSwitch, () =>
        {
            captures++;
            captureOnOwner = Dispatcher.UIThread.CheckAccess();
            return 1550;
        }, classifier: new GateFunctionClassifier(extractor));
        try
        {
            shell.WorkspaceSwitch.Current = Workspace.Logic;
            view.RequestRebuild();
            captures.ShouldBe(0, "scheduled requests must return before reading the live graph");
            await extractor.Barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            captures.ShouldBe(1, "the superseded workspace-entry request must not repeat snapshot capture");
            captureOnOwner.ShouldBeTrue("mutable live state remains confined to its owning UI context");
            extractor.SolvedOnUiThread.ShouldBeFalse();
            Dispatcher.UIThread.CheckAccess().ShouldBeTrue("the owner remains available while classification is held");
            view.PendingRebuild.IsCompleted.ShouldBeFalse();
            extractor.Barrier.Release.TrySetResult();
            await view.PendingRebuild.WaitAsync(TimeSpan.FromSeconds(10));
            view.Graph.Nodes.ShouldHaveSingleItem().Group.ShouldBeSameAs(gate);
            view.IsStale.ShouldBeFalse();
        }
        finally
        {
            extractor.Barrier.Release.TrySetResult();
            await view.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task StopBeforeScheduledOwnerTurn_DoesNotCaptureOrStartClassification()
    {
        await using var shell = MainViewModelTestHelper.CreateMainViewModel();
        await shell.GateView.StopAsync();
        shell.Canvas.Components.Add(new ComponentViewModel(LogicSnapshotFixture.Gate("retired")));
        var captures = 0;
        var extractor = new GateViewSnapshotFixture.CountingExtractor();
        var view = new GateViewViewModel(shell.Canvas, shell.WorkspaceSwitch, () =>
        {
            captures++;
            return 1550;
        }, classifier: new GateFunctionClassifier(extractor));
        try
        {
            shell.WorkspaceSwitch.Current = Workspace.Logic;
            var stopping = view.StopAsync();
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            captures.ShouldBe(0, "retirement revokes queued work before its live-state capture");
            extractor.Calls.ShouldBe(0);
            view.Graph.ShouldBeSameAs(GateViewGraph.Empty);
        }
        finally { await view.StopAsync(); }
    }

    [AvaloniaFact]
    public async Task ExplicitRebuildNow_PreservesImmediateSnapshotCapture()
    {
        await using var shell = MainViewModelTestHelper.CreateMainViewModel();
        await shell.GateView.StopAsync();
        shell.Canvas.Components.Add(new ComponentViewModel(LogicSnapshotFixture.Gate("explicit")));
        var captures = 0;
        var view = new GateViewViewModel(shell.Canvas, shell.WorkspaceSwitch, () =>
        {
            captures++;
            Dispatcher.UIThread.CheckAccess().ShouldBeTrue();
            return 1550;
        });
        try
        {
            var running = view.RebuildNowAsync();
            captures.ShouldBe(1, "the explicit API captures before its caller can mutate inputs");
            await running.WaitAsync(TimeSpan.FromSeconds(10));
            view.Graph.Nodes.Count.ShouldBe(1);
        }
        finally { await view.StopAsync(); }
    }

    private sealed class HeldThreadRecordingExtractor : TruthTableExtractor
    {
        internal LogicSnapshotFixture.WorkerBarrier Barrier { get; } = new();
        internal bool SolvedOnUiThread { get; private set; }

        protected override async Task<TruthTable> SolveSnapshotAsync(TruthTableSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            SolvedOnUiThread = Dispatcher.UIThread.CheckAccess();
            await Barrier.WaitAsync();
            return await base.SolveSnapshotAsync(snapshot, cancellationToken);
        }
    }
}
