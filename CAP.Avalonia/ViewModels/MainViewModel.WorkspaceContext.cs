using System.ComponentModel;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.ContextPanels;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// What the active workspace changes around the canvas besides Run and the dock: the left
/// rail's default section and icon order (Gates in Logic, Chiplets in System), the Truth
/// Table / Make gate section
/// of the properties panel (Logic and System only), and the Probe tool (Layout and Optics only).
/// </summary>
public partial class MainViewModel
{
    /// <summary>The left rail's Gates panel (Logic workspace default).</summary>
    public GatesPanelViewModel GatesPanel { get; private set; } = null!;

    /// <summary>
    /// True while the properties panel shows the Truth Table / Make gate section: exactly one
    /// group is selected and the workspace is Logic or System.
    /// </summary>
    public bool IsGateSectionVisible => WorkspaceSwitch.ShowsGateTools && RightPanel.TruthTable.IsGroupSelected;

    /// <summary>Builds the contextual panels and follows workspace switches; call after <see cref="InitializeWorkspaces"/>.</summary>
    private void InitializeWorkspaceContext()
    {
        GatesPanel = new GatesPanelViewModel(LeftPanel.ComponentLibrary, Home.Examples)
        {
            OpenExampleRequested = FileOperations.OpenDesignAsCopyAsync,
            MakeGateRequested = () => MakeGateCommand.Execute(null),
            ShowLibraryRequested = () => LeftRail.ShowLibraryCommand.Execute(null),
        };
        SystemOverview.ChipletActivated = SelectAndShowChiplet;
        RightPanel.TruthTable.PropertyChanged += OnTruthTablePropertyChanged;
        WorkspaceSwitch.PropertyChanged += OnWorkspaceContextChanged;
        LeftRail.PropertyChanged += OnLeftRailChanged;
        LeftRail.ApplyWorkspace(WorkspaceSwitch.Current);
    }

    private void OnWorkspaceContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceViewModel.Current))
            return;
        LeftRail.ApplyWorkspace(WorkspaceSwitch.Current);
        if (!WorkspaceSwitch.ShowsProbeTool && CanvasInteraction.CurrentMode == InteractionMode.Probe)
            CanvasInteraction.SetSelectModeCommand.Execute(null);
        OnPropertyChanged(nameof(IsGateSectionVisible));
    }

    private void OnTruthTablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TruthTableViewModel.IsGroupSelected))
            OnPropertyChanged(nameof(IsGateSectionVisible));
    }

    private void OnLeftRailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Shell.LeftRailViewModel.IsChipletsActive) && LeftRail.IsChipletsActive)
            RefreshSystemOverview();
    }

    /// <summary>
    /// Opening the gate tools from a workspace that hides them (context menu "Make gate…",
    /// a Logic-tab gate candidate) moves to Logic so the steps are visible.
    /// </summary>
    private void EnsureGateToolsVisible()
    {
        if (!WorkspaceSwitch.ShowsGateTools)
            WorkspaceSwitch.Current = Workspace.Logic;
    }

    /// <summary>Chiplets panel click: selects the chiplet's group and centers the canvas on it.</summary>
    private void SelectAndShowChiplet(ComponentGroup group)
    {
        var groupVm = Canvas.Components.FirstOrDefault(c => ReferenceEquals(c.Component, group));
        if (groupVm == null)
            return;
        Canvas.Selection.SelectSingle(groupVm);
        Canvas.SelectedComponent = groupVm;
        CanvasInteraction.SelectedComponent = groupVm;
        ViewportControl.NavigateCanvasTo(
            group.PhysicalX + group.WidthMicrometers / 2,
            group.PhysicalY + group.HeightMicrometers / 2);
    }
}
