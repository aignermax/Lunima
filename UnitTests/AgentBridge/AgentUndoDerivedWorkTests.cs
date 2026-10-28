using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Documents;
using CAP_Core.Routing;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using UnitTests.Dies;
using Xunit;

namespace UnitTests.AgentBridge;

[Collection("LocalizationSingleton")]
public sealed class AgentUndoDerivedWorkTests
{
    [AvaloniaFact]
    public async Task DisposingOwner_RetainsDocumentAdmissionUntilDerivedWorkerDrains()
    {
        var canvas = CreateSmallCanvas();
        var worker = (HeldUndoGrid)canvas.Routing;
        worker.Hold = true;
        var edit = new AgentDocumentOperation(canvas);
        edit.Commit(() => { _ = canvas.Dies.ApplyScopeBoundsInBackground(); });
        Task disposing = Task.CompletedTask;
        Task retiring = Task.CompletedTask;
        try
        {
            await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            disposing = edit.DisposeAsync().AsTask();
            retiring = canvas.Document.RetireAsync();

            disposing.IsCompleted.ShouldBeFalse();
            retiring.IsCompleted.ShouldBeFalse("the document still owns the accepted, held grid worker");
            worker.Release.TrySetResult();
            await disposing.WaitAsync(TimeSpan.FromSeconds(15));
            await retiring.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            worker.Release.TrySetResult();
            await edit.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(15));
            canvas.DisposeDocumentBindings();
        }
    }

    [AvaloniaFact]
    public async Task SameOwnerLaterCommit_GridContainsLatestGeometryInsteadOfItsEarlierCapture()
    {
        var canvas = CreateSmallCanvas();
        var view = canvas.AddComponent(DieTestFixtures.Part("obstacle", 50, 50, 40, 40));
        await canvas.RecalculateRoutesAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var worker = (HeldUndoGrid)canvas.Routing;
        worker.Hold = true;
        var edit = new AgentDocumentOperation(canvas);
        edit.Commit(() => { _ = canvas.Dies.ApplyScopeBoundsInBackground(); });
        try
        {
            await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            edit.Commit(() => canvas.MoveComponent(view, 100, 0));
            view.X.ShouldBe(150);
            worker.Release.TrySetResult();
            await edit.CompleteDerivedWorkAsync().WaitAsync(TimeSpan.FromSeconds(15));

            var grid = canvas.Router.PathfindingGrid.ShouldNotBeNull();
            var previous = grid.PhysicalToGrid(70, 70);
            var current = grid.PhysicalToGrid(170, 70);
            grid.IsBlockedByComponentOnly(previous.gridX, previous.gridY).ShouldBeFalse(
                "rebasing the operation must not make its earlier geometry current again");
            grid.IsBlockedByComponentOnly(current.gridX, current.gridY).ShouldBeTrue();
            edit.EnsureCurrent();
        }
        finally
        {
            worker.Release.TrySetResult();
            await edit.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(15));
            canvas.DisposeDocumentBindings();
        }
    }

    private static DesignCanvasViewModel CreateSmallCanvas()
    {
        var canvas = new DesignCanvasViewModel(new WaveguideRouter(),
            (router, manager, components, connections, document) => new HeldUndoGrid(router, manager, components, connections, document));
        canvas.SetRootChipSize(400, 200);
        return canvas;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GridCancellationCleanup_AllowsUiHeartbeatWhileWorkerCallbackIsHeld(bool route)
    {
        var canvas = CreateSmallCanvas();
        var worker = (HeldUndoGrid)canvas.Routing;
        worker.Hold = true;
        worker.BlockCancellation = true;
        worker.HoldRoutes = route;
        var running = route ? canvas.RecalculateRoutesAsync() : canvas.Dies.ApplyScopeBoundsInBackground();
        try
        {
            await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            canvas.Document.Invalidate();
            await worker.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var posting = Task.Run(async () =>
            {
                await worker.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(50);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => heartbeat.TrySetResult());
            });
            worker.Release.TrySetResult();
            await heartbeat.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
            running.IsCompleted.ShouldBeFalse("cancellation callback cleanup is still owned, without blocking the UI");
            worker.CallbackRelease.Set();
            await posting.WaitAsync(TimeSpan.FromSeconds(5));
            await running.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            worker.Release.TrySetResult();
            worker.CallbackRelease.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
            await worker.HeldRegistration.DisposeAsync();
            worker.CallbackRelease.Dispose();
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10));
            canvas.DisposeDocumentBindings();
        }
    }

    [AvaloniaTheory]
    [InlineData("complete")]
    [InlineData("close")]
    [InlineData("replace")]
    [InlineData("edit")]
    public async Task UndoTool_OwnsDerivedWorkerUntilCompletion_AndRejectsRevokedPublication(string change)
    {
        var canvas = new DesignCanvasViewModel(new WaveguideRouter(),
            (router, manager, components, connections, document) => new HeldUndoGrid(router, manager, components, connections, document));
        var worker = (HeldUndoGrid)canvas.Routing;
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        var first = canvas.AddComponent(DieTestFixtures.Die("first", 0, 0, 100, 100));
        canvas.AddComponent(DieTestFixtures.Die("second", 300, 0, 100, 100));
        await canvas.RecalculateRoutesAsync().WaitAsync(TimeSpan.FromSeconds(15));
        commands.ExecuteCommand(new MoveComponentCommand(canvas, first, first.X, first.Y, first.X, first.Y + 300));
        await canvas.Dies.PendingGridRebuild.WaitAsync(TimeSpan.FromSeconds(15));
        var originalGrid = canvas.Router.PathfindingGrid.ShouldNotBeNull();
        var routingPasses = worker.RoutingPasses;
        worker.Hold = true;
        var session = new AgentDesignSession(canvas, commands, () => null, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()));
        var tool = new UndoTool(session);
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        var running = catalog.InvokeAsync(tool.Name, JsonSerializer.SerializeToElement(new { }));
        Task closing = Task.CompletedTask;
        try
        {
            await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            running.IsCompleted.ShouldBeFalse("a successful undo response must include its derived grid work");
            canvas.Dies.PendingGridRebuild.IsCompleted.ShouldBeFalse();
            if (change == "close") closing = catalog.CloseAdmissionAsync();
            else if (change == "replace") canvas.Document.ReplaceDocument();
            else if (change == "edit") canvas.Document.Invalidate();
            if (change == "close") closing.IsCompleted.ShouldBeFalse("server stop drains the accepted grid worker");

            worker.Release.TrySetResult();
            if (change == "complete")
            {
                (await running.WaitAsync(TimeSpan.FromSeconds(15))).IsError.ShouldBeFalse();
                canvas.Router.PathfindingGrid.ShouldNotBeSameAs(originalGrid);
                canvas.Router.PathfindingGrid!.MaxY.ShouldBe(canvas.Dies.CurrentBounds().Bottom, 1e-6);
            }
            else
            {
                await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(15)));
                canvas.Router.PathfindingGrid.ShouldBeSameAs(originalGrid, "stale worker results cannot replace current grid state");
            }
            worker.RoutingPasses.ShouldBe(routingPasses, "exact move undo rebuilds its grid without rerouting");
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            worker.Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10));
            canvas.DisposeDocumentBindings();
        }
    }

    private sealed class HeldUndoGrid(WaveguideRouter router, WaveguideConnectionManager manager,
        ObservableCollection<ComponentViewModel> components, ObservableCollection<WaveguideConnectionViewModel> connections,
        DocumentSession document) : RoutingOrchestrator(router, manager, components, connections, document)
    {
        internal bool Hold { get; set; }
        internal bool BlockCancellation { get; set; }
        internal bool HoldRoutes { get; set; }
        internal TaskCompletionSource CallbackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Calculated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim CallbackRelease { get; } = new(false);
        internal CancellationTokenRegistration HeldRegistration { get; private set; }
        internal int RoutingPasses { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<RoutingPassResult?> RunSnapshotAsync(RoutingInputSnapshot input,
            RoutingProgressMailbox progress, CancellationToken token, bool calculateRoutes = true)
        {
            if (calculateRoutes) RoutingPasses++;
            if (Hold && (!calculateRoutes || HoldRoutes))
            {
                if (BlockCancellation)
                    HeldRegistration = token.Register(() =>
                    {
                        CallbackEntered.TrySetResult();
                        // Independent guard guarantees even a broken UI cleanup path cannot hang the host.
                        CallbackRelease.Wait(TimeSpan.FromSeconds(2));
                    });
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                // Finish real detached work even after cancellation, proving final admission and drain.
                var result = await base.RunSnapshotAsync(input, progress, CancellationToken.None, calculateRoutes);
                Calculated.TrySetResult();
                return result;
            }
            return await base.RunSnapshotAsync(input, progress, token, calculateRoutes);
        }
    }
}
