using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.ComponentHelpers;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Workspace half of <see cref="MainViewModel"/>: the toolbar's workspace switch, the
/// workspace-dependent Run action (an on/off toggle of the simulation in Optics and
/// System), and the System workspace's chiplet overview.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The Layout / Optics / Logic / System switch next to Run.</summary>
    public WorkspaceViewModel WorkspaceSwitch { get; private set; } = null!;

    /// <summary>Chiplets and cross-chiplet links shown in the System workspace's dock header.</summary>
    public SystemOverviewViewModel SystemOverview { get; } = new();

    private WorkspaceViewModel InitializeWorkspaces()
    {
        var workspaces = new WorkspaceViewModel(BottomPanel.Analysis);
        FileOperations.CaptureWorkspaceForSave = workspaces.CaptureForSave;
        FileOperations.RestoreWorkspace = stored =>
        {
            workspaces.RestoreAfterLoad(stored, TopLevelComponents(), DesignConnections());
            if (workspaces.IsSystem)
                RefreshSystemOverview();
        };
        workspaces.IsSimulationOn = Canvas.ShowPowerFlow;
        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Canvas.ShowPowerFlow))
                workspaces.IsSimulationOn = Canvas.ShowPowerFlow;
        };
        workspaces.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.Current) && workspaces.IsSystem)
                RefreshSystemOverview();
        };
        BottomPanel.Analysis.Readiness.PlaceLightSourceRequested = ArmLightSourcePlacement;
        return workspaces;
    }

    /// <summary>
    /// The Run button and the L key: runs what the active workspace means by "run".
    /// Layout checks the design; Logic builds the logic network, then steps the clock.
    /// Optics (CW) and System toggle: the first press simulates and shows the power
    /// overlay (System checks the chiplet links first), the next press turns the
    /// simulation off. Optics in Transient mode starts a transient run each press.
    /// </summary>
    [RelayCommand]
    private async Task RunWorkspace()
    {
        switch (WorkspaceSwitch.Current)
        {
            case Workspace.Layout:
                await RunDesignChecks();
                BottomPanel.Analysis.OpenChecks();
                break;
            case Workspace.Optics when SimulationMode == Analysis.SimulationMode.Transient:
                await RunSimulation();
                break;
            case Workspace.Optics:
                if (!TryTurnSimulationOff())
                    await RunSimulation();
                break;
            case Workspace.Logic:
                await RunLogicStep();
                break;
            case Workspace.System:
                if (!TryTurnSimulationOff())
                    await RunSystem();
                break;
        }
    }

    /// <summary>Builds the logic network when none is shown, otherwise clocks it once.</summary>
    private async Task RunLogicStep()
    {
        var logic = RightPanel.Logic;
        BottomPanel.Analysis.OpenLogic();
        if (logic.HasNetwork && logic.StepClockCommand.CanExecute(null))
        {
            logic.StepClockCommand.Execute(null);
            return;
        }
        await logic.BuildNetworkCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// System run: the design checks (including the chiplet-interface alignment and gap
    /// rules), then the CW simulation of the whole design — the S-matrix applies every
    /// cross-chiplet link's coupling loss — and the overview of chiplets and links.
    /// </summary>
    private async Task RunSystem()
    {
        await RunDesignChecks();
        if (SimulationMode != Analysis.SimulationMode.Cw)
            SimulationMode = Analysis.SimulationMode.Cw;
        await ExecuteSimulation();
        RefreshSystemOverview();
        BottomPanel.Analysis.OpenChecks();
    }

    /// <summary>
    /// The P key: the CW simulation on/off toggle in every workspace — the same toggle Run
    /// (L) is in Optics and System. Turns a running simulation off, otherwise runs the CW
    /// simulation and shows its power overlay.
    /// </summary>
    [RelayCommand]
    private async Task TogglePowerOverlay()
    {
        if (TryTurnSimulationOff())
            return;
        await ExecuteSimulation();
    }

    /// <summary>
    /// Turns the CW simulation off (hides the power overlay, stops auto-resimulation) when
    /// it is on. Returns false — and changes nothing — when it was off.
    /// </summary>
    private bool TryTurnSimulationOff()
    {
        if (!Canvas.ShowPowerFlow)
            return false;
        Canvas.ShowPowerFlow = false;
        Canvas.PowerFlowVisualizer.IsEnabled = false;
        StatusText = LocalizationService.Instance.Translate("Status.SimulationOverlayOff");
        return true;
    }

    /// <summary>
    /// Fix for "no light source": arms placement of the first light-source coupler the
    /// library offers that the active process allows (the next canvas click places it).
    /// </summary>
    private void ArmLightSourcePlacement()
    {
        var candidates = LeftPanel.AllTemplates
            .Where(t => LightSourceClassifier.IsLightInjectingCoupler(t.Name))
            .ToList();
        var template = candidates.FirstOrDefault(IsPlacementAllowed) ?? candidates.FirstOrDefault();
        if (template == null)
        {
            StatusText = LocalizationService.Instance.Translate("Readiness.NoLightSourceTemplate");
            return;
        }
        CanvasInteraction.SelectedTemplate = template;
    }

    private bool IsPlacementAllowed(Library.ComponentTemplate template) =>
        CanvasInteraction.PlacementContext.CheckPlacementAt(template.PdkSource, null).IsAllowed;

    /// <summary>Recomputes the System workspace's chiplet and link overview (also the Chiplets pane's refresh button).</summary>
    [RelayCommand]
    public void RefreshSystemOverview()
    {
        var context = CanvasInteraction.PlacementContext;
        var wavelengthNm = Canvas.Components.FirstOrDefault(c => c.IsLightSource)?.LaserConfig?.WavelengthNm
                           ?? StandardWaveLengths.RedNM;
        SystemOverview.Refresh(
            TopLevelComponents(),
            DesignConnections(),
            context.ResolveChipletProcess,
            wavelengthNm);
    }

    private List<CAP_Core.Components.Core.Component> TopLevelComponents() =>
        Canvas.Components.Select(c => c.Component).ToList();

    private List<CAP_Core.Components.Connections.WaveguideConnection> DesignConnections() =>
        Canvas.Connections.Select(c => c.Connection).ToList();
}
