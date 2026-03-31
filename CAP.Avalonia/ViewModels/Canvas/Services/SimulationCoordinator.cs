using CAP.Avalonia.Visualization;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>
/// Coordinates simulation invalidation, re-simulation requests, and power flow display updates.
/// </summary>
public class SimulationCoordinator
{
    private readonly PowerFlowVisualizer _visualizer;

    /// <summary>
    /// Callback invoked when simulation needs to be re-run.
    /// </summary>
    public Action? SimulationRequested { get; set; }

    /// <summary>
    /// Raised when the ShowPowerFlow state should change.
    /// Parameters: (newValue, forceNotify).
    /// </summary>
    public event Action<bool, bool>? ShowPowerFlowChanged;

    /// <summary>
    /// Initializes the simulation coordinator.
    /// </summary>
    public SimulationCoordinator(PowerFlowVisualizer visualizer)
    {
        _visualizer = visualizer;
    }

    /// <summary>
    /// Called when the circuit topology changes. Clears power flow and requests re-simulation if needed.
    /// </summary>
    /// <param name="wasShowingOverlay">Whether the power flow overlay was visible.</param>
    public void InvalidateSimulation(bool wasShowingOverlay)
    {
        _visualizer.Clear();
        ShowPowerFlowChanged?.Invoke(false, false);

        if (wasShowingOverlay)
            SimulationRequested?.Invoke();
    }

    /// <summary>
    /// Called when a component parameter changes. Re-runs simulation without clearing overlay.
    /// </summary>
    public void RequestResimulation(bool showPowerFlow)
    {
        if (showPowerFlow)
            SimulationRequested?.Invoke();
    }

    /// <summary>
    /// Updates the power flow display after simulation completes.
    /// </summary>
    /// <param name="isAlreadyVisible">Whether ShowPowerFlow is currently true.</param>
    public void RefreshPowerFlowDisplay(bool isAlreadyVisible)
    {
        _visualizer.IsEnabled = true;

        if (isAlreadyVisible)
            ShowPowerFlowChanged?.Invoke(true, true);
        else
            ShowPowerFlowChanged?.Invoke(true, false);
    }
}
