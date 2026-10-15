using System.ComponentModel;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.GateView;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.ComponentHelpers;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Gate-view half of <see cref="MainViewModel"/>: the Logic workspace's gate/physical view
/// toggle, its jump back to Layout, and the hand-over to the physical view when a
/// waveguide tool is picked.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The Logic workspace's gate-level view of the design.</summary>
    public GateViewViewModel GateView { get; private set; } = null!;

    /// <summary>Builds the gate view; call after <see cref="InitializeWorkspaces"/>.</summary>
    private void InitializeGateView()
    {
        GateView = new GateViewViewModel(Canvas, WorkspaceSwitch, ResolveLogicWavelengthNm, LogicInputBits)
        {
            ShowInLayoutRequested = ShowInLayout,
        };
        CanvasInteraction.PropertyChanged += OnInteractionModeChangedForGateView;
        if (GateView.IsShown)
            GateView.RequestRebuild();
    }

    private void OnInteractionModeChangedForGateView(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CanvasInteractionViewModel.CurrentMode))
            return;
        if (GateView.OnInteractionModeChanged(CanvasInteraction.CurrentMode))
            StatusText = LocalizationService.Instance.Translate("GateView.SwitchedToPhysical");
    }

    /// <summary>Switches to Layout with <paramref name="target"/> selected and centred.</summary>
    private void ShowInLayout(ComponentViewModel target)
    {
        WorkspaceSwitch.Current = Workspace.Layout;
        Canvas.Selection.SelectSingle(target);
        Canvas.SelectedComponent = target;
        CanvasInteraction.SelectedComponent = target;
        ViewportControl.NavigateCanvasTo(target.X + target.Width / 2, target.Y + target.Height / 2);
    }

    /// <summary>The wavelength gates are classified at: the active laser's, as the Logic tab uses.</summary>
    private int ResolveLogicWavelengthNm() =>
        Canvas.Components.FirstOrDefault(c => c.IsLightSource)?.LaserConfig?.WavelengthNm
        ?? StandardWaveLengths.RedNM;

    /// <summary>The Logic tab's input toggles by network input name (empty without a built network).</summary>
    private IReadOnlyDictionary<string, bool> LogicInputBits() =>
        RightPanel.Logic.Inputs
            .GroupBy(input => input.PinName)
            .ToDictionary(group => group.Key, group => group.First().IsOn);
}
