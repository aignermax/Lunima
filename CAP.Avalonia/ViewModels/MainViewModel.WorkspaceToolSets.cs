using System.ComponentModel;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.ToolSets;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Tool-set half of the workspaces: each workspace places only its own kind of object
/// (<see cref="WorkspaceToolSet"/>) — the library list, the canvas placement paths (click,
/// drag &amp; drop, armed templates) and the context menu all follow the same rule — and every
/// refusal comes with a one-click switch to Layout, where everything is placeable.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Workspace-dependent entries of the canvas context menu.</summary>
    public CanvasContextMenuViewModel CanvasContextMenu { get; } = new();

    /// <summary>The active workspace's tool set.</summary>
    public WorkspaceToolSet ToolSet => WorkspaceSwitch.Profile.ToolSet;

    /// <summary>True where the library pane lists only instruments (Optics): shows the "place circuit parts in Layout" hint.</summary>
    public bool IsInstrumentLibrary => ToolSet.Library == LibraryScope.Instruments;

    /// <summary>True where the library pane lists only the edge couplers that link dies (System).</summary>
    public bool IsDieFacetLibrary => ToolSet.Library == LibraryScope.DieFacets;

    /// <summary>True where the library pane shows saved groups, PDK macros and its import / new-component tools (Layout).</summary>
    public bool IsFullLibrary => ToolSet.Library == LibraryScope.AllComponents;

    /// <summary>Follows workspace switches; call after <see cref="InitializeWorkspaceContext"/>.</summary>
    private void InitializeWorkspaceToolSets()
    {
        CanvasInteraction.ComponentPlacementRefusal = t => ToolSet.CanPlace(t) ? null : PlacementRefusal(t.Name);
        CanvasInteraction.GroupPlacementRefusal = t => ToolSet.CanPlace(t) ? null : PlacementRefusal(t.Name);
        LeftRail.SectionUnavailable += _ => WorkspaceSwitch.Current = Workspace.Layout;
        WorkspaceSwitch.PropertyChanged += OnWorkspaceToolSetChanged;
        CanvasInteraction.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanvasInteractionViewModel.SelectedComponent))
                RefreshCanvasContextMenu();
        };
        ApplyWorkspaceToolSet();
    }

    private void OnWorkspaceToolSetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.Current))
            ApplyWorkspaceToolSet();
    }

    private void ApplyWorkspaceToolSet()
    {
        var toolSet = ToolSet;
        LeftPanel.ApplyWorkspaceScope(toolSet.CanPlace);
        DisarmRefusedTemplate(toolSet);
        RefreshCanvasContextMenu();
        OnPropertyChanged(nameof(ToolSet));
        OnPropertyChanged(nameof(IsInstrumentLibrary));
        OnPropertyChanged(nameof(IsDieFacetLibrary));
        OnPropertyChanged(nameof(IsFullLibrary));
    }

    /// <summary>A template armed in the previous workspace must not place in this one.</summary>
    private void DisarmRefusedTemplate(WorkspaceToolSet toolSet)
    {
        bool armedRefused = CanvasInteraction.SelectedTemplate is { } t && !toolSet.CanPlace(t)
                            || CanvasInteraction.SelectedGroupTemplate is { } g && !toolSet.CanPlace(g);
        if (armedRefused)
            CanvasInteraction.SetSelectModeCommand.Execute(null);
    }

    private void RefreshCanvasContextMenu() =>
        CanvasContextMenu.Refresh(WorkspaceSwitch.Current, CanvasInteraction.SelectedComponent);

    private string PlacementRefusal(string templateName) => string.Format(
        LocalizationService.Instance.Translate("Workspace.ToolSet.PlacementRefused"),
        templateName, WorkspaceSwitch.DisplayName);

    /// <summary>
    /// The one-click switch behind every "place circuit parts in Layout" hint: Layout shows the
    /// full component library.
    /// </summary>
    [RelayCommand]
    private void SwitchToLayout()
    {
        WorkspaceSwitch.Current = Workspace.Layout;
        LeftRail.Show(LeftRailSection.Library);
    }

    /// <summary>Context menu "Show in Layout": switches to Layout centred on the element under the cursor.</summary>
    [RelayCommand]
    private void ShowInLayout()
    {
        var target = CanvasInteraction.SelectedComponent;
        WorkspaceSwitch.Current = Workspace.Layout;
        if (target != null)
            ViewportControl.NavigateCanvasTo(target.X + target.Width / 2, target.Y + target.Height / 2);
    }
}
