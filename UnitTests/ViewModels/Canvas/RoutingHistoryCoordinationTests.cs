using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using Xunit;

namespace UnitTests.ViewModels.Canvas;

public partial class RoutingOrchestratorProgressTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossingJournal_AttributesLateWorkerChangeToItsStartingCommand(bool clearHistory)
    {
        var canvas = new DesignCanvasViewModel();
        var journal = new CrossingUndoJournal(canvas, _ => { }, _ => { });
        var commands = new CAP.Avalonia.Commands.CommandManager();
        journal.Attach(commands);
        var first = new HistoryMarker();
        var second = new HistoryMarker();
        commands.ExecuteCommand(first);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Run(async () =>
        {
            await release.Task;
            journal.Record(new CAP_Core.Routing.CrossingInsertion.CrossingChange([], [], [], () => { }));
        });
        if (clearHistory) commands.ClearHistory();
        commands.ExecuteCommand(second);
        if (clearHistory)
            journal.Record(new CAP_Core.Routing.CrossingInsertion.CrossingChange([], [], [], () => { }));
        release.SetResult();
        await worker;
        journal.ChangeCount(first).ShouldBe(clearHistory ? 0 : 1);
        journal.ChangeCount(second).ShouldBe(clearHistory ? 1 : 0);
    }

    private sealed class HistoryMarker : CAP.Avalonia.Commands.IUndoableCommand
    {
        public string Description => "history marker";
        public void Execute() { }
        public void Undo() { }
    }

    [Fact]
    public async Task EmptyBridgeUndo_PreservesQueuedCrossingCanvasUpdates()
    {
        var canvas = new DesignCanvasViewModel();
        var callbacks = new Queue<Action>();
        var binder = new CrossingInsertionCanvasBinder(canvas, () => null, callbacks.Enqueue);
        var crossing = CrossingTestCircuit.CreateCrossingComponent();
        binder.Service.ComponentAdded!(crossing);
        var commands = new CAP.Avalonia.Commands.CommandManager();
        var session = new CAP.Avalonia.Services.AgentBridge.Tools.AgentDesignSession(
            canvas, commands, () => null, new CAP.Avalonia.Services.ExampleDesignsService());
        var result = await new CAP.Avalonia.Services.AgentBridge.Tools.Edit.UndoTool(session)
            .ExecuteAsync(System.Text.Json.JsonDocument.Parse("{}").RootElement);
        result.IsError.ShouldBeTrue();
        callbacks.Dequeue()();
        canvas.Components.ShouldContain(vm => vm.Component == crossing);
    }

    [AvaloniaFact]
    public async Task HistoryChange_WaitsForCancelledWorker_ThenRestoresAfterItsLastWrite()
    {
        var orchestrator = CreateOrchestratorWithDeferredConnections(1);
        using var parked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var value = "before";
        orchestrator.BuildConnectionProcessFloorProvider = () => (_, _) =>
        {
            parked.Set();
            release.Wait();
            value = "late worker write";
            return null;
        };
        var routing = orchestrator.RecalculateRoutesAsync();
        Task<bool>? history = null;
        try
        {
            parked.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            history = orchestrator.ExecuteHistoryChangeAsync(() => { value = "restored"; return true; }, () => true);
            history.IsCompleted.ShouldBeFalse("cancellation must drain the mutating worker first");
            value.ShouldBe("before");
        }
        finally
        {
            release.Set();
            await PumpUntilDone(history == null ? routing : Task.WhenAll(routing, history));
        }
        value.ShouldBe("restored");
        orchestrator.IsRouting.ShouldBeFalse();
        (await orchestrator.ExecuteHistoryChangeAsync(() => true, () => true)).ShouldBeTrue("the gate remains usable");
    }

    [Fact]
    public async Task HistoryChange_RejectsPreviouslyQueuedCrossingCanvasUpdates()
    {
        var canvas = new DesignCanvasViewModel();
        var callbacks = new Queue<Action>();
        var binder = new CrossingInsertionCanvasBinder(canvas, () => null, callbacks.Enqueue);
        var staleCrossing = CrossingTestCircuit.CreateCrossingComponent();
        binder.Service.ComponentAdded!(staleCrossing);
        callbacks.Count.ShouldBe(1);
        await canvas.Routing.ExecuteHistoryChangeAsync(() => true, () => true);
        callbacks.Dequeue()();
        canvas.Components.ShouldNotContain(vm => vm.Component == staleCrossing);
    }
}
