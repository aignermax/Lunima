using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.ComponentHelpers;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Workspace half of <see cref="MainViewModel"/>: the toolbar's workspace switch, the
/// workspace-dependent Run action, the power-overlay toggle beside Run, and the System
/// workspace's chiplet overview.
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
    /// Layout checks the design; Optics simulates (CW or transient); Logic builds the
    /// logic network, then steps the clock; System checks the chiplet links and runs
    /// the CW simulation over the whole multi-chiplet design.
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
            case Workspace.Optics:
                await RunSimulation();
                break;
            case Workspace.Logic:
                await RunLogicStep();
                break;
            case Workspace.System:
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
    /// The eye button beside Run and the P key: shows or hides the CW power-flow overlay.
    /// Showing it without a result yet runs the CW simulation first.
    /// </summary>
    [RelayCommand]
    private async Task TogglePowerOverlay()
    {
        if (Canvas.ShowPowerFlow)
        {
            Canvas.ShowPowerFlow = false;
            Canvas.PowerFlowVisualizer.IsEnabled = false;
            StatusText = LocalizationService.Instance.Translate("Overlay.PowerFlowOff");
            return;
        }

        if (Canvas.PowerFlowVisualizer.CurrentResult == null)
        {
            await ExecuteSimulation();
        }
        else
        {
            Canvas.ShowPowerFlow = true;
            Canvas.PowerFlowVisualizer.IsEnabled = true;
            StatusText = LocalizationService.Instance.Translate("Overlay.PowerFlowOn");
        }
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

    /// <summary>Recomputes the System workspace's chiplet and link overview.</summary>
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
