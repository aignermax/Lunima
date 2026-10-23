using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Documents;
using CAP_Core.Routing;

namespace UnitTests.ViewModels.Canvas;

internal sealed class RoutingWorkerFixture
{
    internal DocumentSession Document { get; } = new();
    internal WaveguideRouter Router { get; } = new() { Phase1MaxNodes = 2000, Phase2MaxNodes = 5000 };
    internal WaveguideConnectionManager Manager { get; }
    internal Component Start { get; } = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
    internal Component End { get; } = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
    internal WaveguideConnection Wire { get; }
    internal ControlledRoutingWorker Orchestrator { get; }

    internal RoutingWorkerFixture()
    {
        Start.PhysicalX = 0;
        End.PhysicalX = 200;
        Manager = new WaveguideConnectionManager(Router);
        Wire = new WaveguideConnection { StartPin = Start.PhysicalPins[1], EndPin = End.PhysicalPins[0] };
        Manager.AddExistingConnection(Wire);
        ObservableCollection<ComponentViewModel> components = [new(Start), new(End)];
        ObservableCollection<WaveguideConnectionViewModel> connections = [new(Wire)];
        Orchestrator = new(Router, Manager, components, connections, Document);
        Orchestrator.InitializeAStarRouting(-40, -40, 300, 100);
    }

    internal async Task FinishAsync(Task running)
    {
        Orchestrator.Begin.TrySetResult();
        Orchestrator.Finish.TrySetResult();
        try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { Orchestrator.FlushUpdates(); }
    }
}

internal sealed class ControlledRoutingWorker(WaveguideRouter router, WaveguideConnectionManager manager,
    ObservableCollection<ComponentViewModel> components, ObservableCollection<WaveguideConnectionViewModel> connections,
    DocumentSession document) : RoutingOrchestrator(router, manager, components, connections, document)
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Begin { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Calculated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Action> _updates = new();

    protected override async Task<T> RunOnWorkerAsync<T>(Func<T> work)
    {
        Entered.TrySetResult();
        await Begin.Task.WaitAsync(TimeSpan.FromSeconds(8));
        try
        {
            var result = await Task.Run(work);
            Calculated.TrySetResult();
            await Finish.Task.WaitAsync(TimeSpan.FromSeconds(8));
            return result;
        }
        catch (Exception error)
        {
            Calculated.TrySetException(error);
            throw;
        }
    }

    protected override void PostUiUpdate(Action update) => _updates.Enqueue(update);

    internal void FlushUpdates()
    {
        while (_updates.TryDequeue(out var update)) update();
    }
}
