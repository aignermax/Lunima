using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.Presentation;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Presentation half of the workspaces: on every switch the entered workspace's
/// <see cref="WorkspacePresentation"/> decides the canvas overlays (power flow, design-check
/// markers) and the drawing emphasis, and the overlays the user left a workspace with come
/// back when they return. Only the view changes — the design, undo history and every tool
/// stay as they are.
/// </summary>
public partial class MainViewModel
{
    private readonly WorkspaceOverlayMemory _overlayMemory = new();

    /// <summary>Follows workspace switches; call after <see cref="InitializeWorkspaces"/>.</summary>
    private void InitializeWorkspacePresentation()
    {
        Canvas.ViewSettings.Emphasis = WorkspaceSwitch.Profile.Presentation.Emphasis;
        WorkspaceSwitch.Switched += ApplyWorkspacePresentation;
        CommandManager.StateChanged += ClearFindingsOnHistoryChange;
    }

    private void ClearFindingsOnHistoryChange(object? sender, EventArgs args) => Canvas.ViewSettings.ClearDesignCheckFindings();

    private void UnwireWorkspacePresentation()
    {
        WorkspaceSwitch.Switched -= ApplyWorkspacePresentation;
        CommandManager.StateChanged -= ClearFindingsOnHistoryChange;
    }

    /// <summary>The overlays on screen right now.</summary>
    private WorkspaceOverlayState CurrentOverlays() =>
        new(Canvas.ShowPowerFlow, Canvas.ViewSettings.ShowDesignCheckMarkers);

    private void ApplyWorkspacePresentation(Workspace left, Workspace entered)
    {
        _overlayMemory.Remember(left, CurrentOverlays());
        var presentation = WorkspaceProfile.For(entered).Presentation;
        var target = _overlayMemory.Resolve(entered, presentation, CurrentOverlays(),
            hasSimulationResult: Canvas.PowerFlowVisualizer.CurrentResult != null);

        Canvas.ViewSettings.Emphasis = presentation.Emphasis;
        Canvas.ViewSettings.ShowDesignCheckMarkers = target.DesignCheckMarkers;
        ApplyPowerFlow(target.PowerFlow);
    }

    private void ApplyPowerFlow(bool on)
    {
        if (on == Canvas.ShowPowerFlow)
            return;
        if (on)
            ResumeSimulation();
        else
            TryTurnSimulationOff();
    }

    /// <summary>
    /// Turns the CW simulation back on without blocking the switch: the overlay shows the
    /// last result at once and the live scheduler re-simulates the current design off the UI
    /// thread. Skipped in Transient mode (no CW overlay) and on a design without a light
    /// source, which has nothing to show.
    /// </summary>
    private void ResumeSimulation()
    {
        if (SimulationMode == Analysis.SimulationMode.Transient || !DesignHasLightSource())
            return;
        Canvas.PowerFlowVisualizer.IsEnabled = true;
        Canvas.ShowPowerFlow = true;
        LiveSimulation.Request();
    }

    private bool DesignHasLightSource() =>
        WorkspaceDefaults.Flatten(TopLevelComponents()).Any(Services.SimulationService.IsLightSource);

    /// <summary>A newly opened design has no design-check findings yet.</summary>
    private void ResetWorkspacePresentationForNewDesign() => Canvas.ViewSettings.ClearDesignCheckFindings();
}
