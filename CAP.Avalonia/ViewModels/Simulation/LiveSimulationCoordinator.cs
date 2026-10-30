using System.ComponentModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Simulation;

/// <summary>Owns live simulation admission, event wiring, and asynchronous retirement.</summary>
internal sealed class LiveSimulationCoordinator
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commands;
    private bool _stopping;

    public LiveSimulationCoordinator(DesignCanvasViewModel canvas, CommandManager commands,
        Func<CancellationToken, Task> simulate)
    {
        _canvas = canvas;
        _commands = commands;
        Scheduler = new(() => canvas.ShowPowerFlow, () => canvas.IsRouting, simulate);
        canvas.SimulationRequested = Scheduler.Request;
        canvas.PropertyChanged += CanvasChanged;
        commands.StateChanged += DesignChanged;
    }

    public LiveSimulationScheduler Scheduler { get; }

    public Task StopAsync()
    {
        if (!_stopping)
        {
            _stopping = true;
            _canvas.PropertyChanged -= CanvasChanged;
            _commands.StateChanged -= DesignChanged;
            if (_canvas.SimulationRequested == Scheduler.Request) _canvas.SimulationRequested = null;
        }
        return Scheduler.StopAsync();
    }

    private void DesignChanged(object? sender, EventArgs args) => Scheduler.Request();

    private void CanvasChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DesignCanvasViewModel.IsRouting) && !_canvas.IsRouting)
            Scheduler.OnRoutingFinished();
        else if (args.PropertyName == nameof(DesignCanvasViewModel.ShowPowerFlow) && !_canvas.ShowPowerFlow)
            Scheduler.Cancel();
    }
}
