using System.ComponentModel;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Simulation;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Live-simulation half of <see cref="MainViewModel"/>: while the CW simulation is on
/// (power overlay shown), every design change — undoable edits, undo/redo, canvas
/// invalidations, parameter edits, finished routing passes and opening another design —
/// re-runs it through the debounced, cancellable <see cref="LiveSimulationScheduler"/>.
/// </summary>
public partial class MainViewModel
{
    private bool _liveRunDeferred;
    private LiveSimulationCoordinator _liveSimulation = null!;

    /// <summary>Re-runs the switched-on simulation after design changes.</summary>
    public LiveSimulationScheduler LiveSimulation => _liveSimulation.Scheduler;

    private void InitializeLiveSimulation()
    {
        _liveSimulation = new LiveSimulationCoordinator(Canvas, CommandManager,
            token => ExecuteSimulation(token, isLiveRun: true));
    }

    /// <summary>
    /// Another design replaced the canvas (load, example, recent project, GDS import,
    /// File → New). A switched-on simulation stays on and re-runs on the new design once
    /// its routing is done; an empty canvas (File → New) switches it off.
    /// </summary>
    private void OnProjectOpened()
    {
        var cameFromHome = Home.IsHomeVisible;
        Home.OnProjectOpened();
        ResetWorkspacePresentationForNewDesign();
        _learning.Guidance.ProjectOpened(cameFromHome);
        if (!Canvas.ShowPowerFlow)
            return;
        if (Canvas.Components.Count == 0)
        {
            TryTurnSimulationOff();
            return;
        }
        Canvas.PowerFlowVisualizer.Clear();
        LiveSimulation.Request();
    }

    /// <summary>
    /// Shows why the simulation could not run. A live re-run that fails switches the
    /// simulation off, so the overlay never keeps showing light of a design that no
    /// longer exists.
    /// </summary>
    private void ReportSimulationFailure(string message, bool isLiveRun)
    {
        if (isLiveRun)
            TryTurnSimulationOff();
        StatusText = message;
    }

    /// <summary>A live run skipped because another simulation was running is queued again.</summary>
    private void RequeueDeferredLiveRun()
    {
        if (!_liveRunDeferred)
            return;
        _liveRunDeferred = false;
        LiveSimulation.Request();
    }
}
