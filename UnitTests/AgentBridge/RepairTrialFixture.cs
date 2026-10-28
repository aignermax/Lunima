using System.Collections.ObjectModel;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Documents;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.AgentBridge;

internal sealed class RepairTrialFixture
{
    internal RepairTrialGate Gate { get; } = new();
    internal DesignCanvasViewModel Canvas { get; }
    internal CommandManager Commands { get; } = new();
    internal AgentDesignSession Session { get; }
    internal WaveguideConnection Wire { get; }
    internal AgentToolCatalog Catalog { get; }
    private readonly (Component Component, double X, double Y)[] _positions;
    private readonly WaveguideConnection[] _order;
    private readonly RoutedPath?[] _paths;
    private readonly int _undo;
    private readonly int _redo;

    internal int RoutingOwnersCreated { get; private set; }

    internal RepairTrialFixture(RepairGraphBuilder? graphBuilder = null)
    {
        CanvasRoutingFactory factory = (router, manager, components, connections, document) =>
        {
            RoutingOwnersCreated++;
            return new HeldRepairRouting(router, manager, components, connections, document, Gate);
        };
        Canvas = new DesignCanvasViewModel(new WaveguideRouter { Phase1MaxNodes = 2000, Phase2MaxNodes = 5000 }, factory);
        var first = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        first.Name = "first";
        var last = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        last.Name = "last";
        last.PhysicalX = 200;
        var firstView = Canvas.AddComponent(first);
        var lastView = Canvas.AddComponent(last);
        Wire = new WaveguideConnection { StartPin = first.PhysicalPins[1], EndPin = last.PhysicalPins[0] };
        Canvas.ConnectionManager.AddExistingConnection(Wire);
        Canvas.Connections.Add(new(Wire));
        Canvas.InitializeAStarRouting(-40, -40, 300, 100);
        using (Canvas.Routing.SuspendAutomaticRouting())
        {
            Commands.ExecuteCommand(new MoveComponentCommand(Canvas, firstView, 0, 0, 4, 0));
            Commands.ExecuteCommand(new MoveComponentCommand(Canvas, lastView, 200, 0, 204, 0));
            Commands.Undo().ShouldBeTrue();
        }
        var blocked = new RoutedPath { IsBlockedFallback = true };
        blocked.Segments.Add(new StraightSegment(24, 0, 200, 0, 0));
        Wire.RestoreCachedPath(blocked);
        _positions = Canvas.Components.Select(view => (view.Component, view.X, view.Y)).ToArray();
        _order = Canvas.ConnectionManager.Connections.ToArray();
        _paths = _order.Select(wire => wire.RoutedPath).ToArray();
        _undo = Commands.UndoCount;
        _redo = Commands.RedoCount;
        _undo.ShouldBe(1);
        _redo.ShouldBe(1);
        Session = new AgentDesignSession(Canvas, Commands, () => null, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()));
        Catalog = new AgentToolCatalog([new SuggestFixesTool(Session, factory, graphBuilder)], new AiToolRegistry([]), new InlineUiDispatcher());
    }

    internal Task<AgentToolResult> RunAsync(CancellationToken cancellationToken = default) => Catalog.InvokeAsync(
        "suggest_fixes", JsonSerializer.SerializeToElement(new { connection_id = Wire.Id.ToString(), max_trials = 1 }), cancellationToken);

    internal void AssertLiveUnchanged()
    {
        Canvas.Components.Select(view => view.Component).ShouldBe(_positions.Select(position => position.Component));
        foreach (var (component, x, y) in _positions)
        {
            component.PhysicalX.ShouldBe(x);
            component.PhysicalY.ShouldBe(y);
        }
        Canvas.ConnectionManager.Connections.ShouldBe(_order);
        Canvas.Connections.Select(view => view.Connection).ShouldBe(_order);
        for (var index = 0; index < _order.Length; index++)
            _order[index].RoutedPath.ShouldBeSameAs(_paths[index]);
        Commands.UndoCount.ShouldBe(_undo);
        Commands.RedoCount.ShouldBe(_redo);
    }

    internal async Task FinishAsync(Task running)
    {
        Gate.Begin.TrySetResult();
        Gate.Finish.TrySetResult();
        try { await running.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (OperationCanceledException) { }
        await Catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Canvas.DisposeDocumentBindings();
    }
}

internal sealed class RepairTrialGate
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Begin { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Calculated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal async Task<T> RunAsync<T>(Func<T> work)
    {
        Entered.TrySetResult();
        await Begin.Task.WaitAsync(TimeSpan.FromSeconds(15));
        T result = default!;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        try { result = await Task.Run(work); }
        catch (Exception error) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        Calculated.TrySetResult();
        await Finish.Task.WaitAsync(TimeSpan.FromSeconds(15));
        failure?.Throw();
        return result;
    }
}

internal sealed class HeldRepairRouting(WaveguideRouter router, WaveguideConnectionManager manager,
    ObservableCollection<ComponentViewModel> components, ObservableCollection<WaveguideConnectionViewModel> connections,
    DocumentSession document, RepairTrialGate gate) : RoutingOrchestrator(router, manager, components, connections, document)
{
    protected override Task<T> RunOnWorkerAsync<T>(Func<T> work) => gate.RunAsync(work);
    protected override void PostUiUpdate(Action update) { /* Final result still exercises the actual publication path. */ }
}
