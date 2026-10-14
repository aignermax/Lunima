using System.Collections.Generic;
using CAP.Avalonia.ViewModels.Workspaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>
/// State of the main window's left icon rail: which section fills the one full-height pane
/// next to it (VS Code / Figma style). The rail is contextual: each workspace opens on its
/// own section (<see cref="WorkspaceRailLayout"/>), shows only the sections of its tool set in
/// its order, and the rail remembers the section the user last picked in each workspace.
/// Asking for a section the workspace leaves out raises <see cref="SectionUnavailable"/> so the
/// host can switch to Layout (which shows every section) instead of silently refusing.
/// </summary>
public partial class LeftRailViewModel : ObservableObject
{
    /// <summary>
    /// Named controls that live in a rail section. A guided tour that points at one of
    /// them must find it visible, so <see cref="RevealTourTarget"/> switches to its section.
    /// </summary>
    private static readonly Dictionary<string, LeftRailSection> SectionByControlName = new()
    {
        ["ComponentLibraryList"] = LeftRailSection.Library,
        ["ComponentLibraryScroll"] = LeftRailSection.Library,
        ["UserGroupsListBox"] = LeftRailSection.Library,
        ["PdkGroupsListBox"] = LeftRailSection.Library,
    };

    private readonly Dictionary<Workspace, LeftRailSection> _remembered = new();
    private Workspace _workspace = Workspace.Layout;

    /// <summary>The section currently shown in the left pane.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryActive), nameof(IsHierarchyActive), nameof(IsPdksActive),
        nameof(IsGatesActive), nameof(IsChipletsActive))]
    private LeftRailSection _activeSection = LeftRailSection.Library;

    /// <summary>True while the component library is shown.</summary>
    public bool IsLibraryActive => ActiveSection == LeftRailSection.Library;

    /// <summary>True while the design hierarchy is shown.</summary>
    public bool IsHierarchyActive => ActiveSection == LeftRailSection.Hierarchy;

    /// <summary>True while PDK management is shown.</summary>
    public bool IsPdksActive => ActiveSection == LeftRailSection.Pdks;

    /// <summary>True while the Gates panel is shown.</summary>
    public bool IsGatesActive => ActiveSection == LeftRailSection.Gates;

    /// <summary>True while the Chiplets panel is shown.</summary>
    public bool IsChipletsActive => ActiveSection == LeftRailSection.Chiplets;

    /// <summary>
    /// Raised when a section the active workspace does not show is requested (a "show all
    /// components" link, a tour step). The handler switches to a workspace that shows it.
    /// </summary>
    public event Action<LeftRailSection>? SectionUnavailable;

    /// <summary>True when the active workspace's rail shows the library icon.</summary>
    public bool IsLibraryAvailable => IsAvailable(LeftRailSection.Library);

    /// <summary>True when the rail shows the hierarchy icon.</summary>
    public bool IsHierarchyAvailable => IsAvailable(LeftRailSection.Hierarchy);

    /// <summary>True when the rail shows the PDKs icon.</summary>
    public bool IsPdksAvailable => IsAvailable(LeftRailSection.Pdks);

    /// <summary>True when the rail shows the Gates icon.</summary>
    public bool IsGatesAvailable => IsAvailable(LeftRailSection.Gates);

    /// <summary>True when the rail shows the Chiplets icon.</summary>
    public bool IsChipletsAvailable => IsAvailable(LeftRailSection.Chiplets);

    /// <summary>Rail position (grid row) of the library icon in the active workspace.</summary>
    public int LibraryOrder => OrderOf(LeftRailSection.Library);

    /// <summary>Rail position of the hierarchy icon.</summary>
    public int HierarchyOrder => OrderOf(LeftRailSection.Hierarchy);

    /// <summary>Rail position of the PDKs icon.</summary>
    public int PdksOrder => OrderOf(LeftRailSection.Pdks);

    /// <summary>Rail position of the Gates icon.</summary>
    public int GatesOrder => OrderOf(LeftRailSection.Gates);

    /// <summary>Rail position of the Chiplets icon.</summary>
    public int ChipletsOrder => OrderOf(LeftRailSection.Chiplets);

    /// <summary>Shows the component library.</summary>
    [RelayCommand]
    private void ShowLibrary() => Show(LeftRailSection.Library);

    /// <summary>Shows the design hierarchy.</summary>
    [RelayCommand]
    private void ShowHierarchy() => Show(LeftRailSection.Hierarchy);

    /// <summary>Shows PDK management.</summary>
    [RelayCommand]
    private void ShowPdks() => Show(LeftRailSection.Pdks);

    /// <summary>Shows the Gates panel.</summary>
    [RelayCommand]
    private void ShowGates() => Show(LeftRailSection.Gates);

    /// <summary>Shows the Chiplets panel.</summary>
    [RelayCommand]
    private void ShowChiplets() => Show(LeftRailSection.Chiplets);

    /// <summary>
    /// Re-arranges the rail for <paramref name="workspace"/>: icon order per
    /// <see cref="WorkspaceRailLayout"/>, and the section the user last chose there
    /// (or the workspace's default on the first visit).
    /// </summary>
    public void ApplyWorkspace(Workspace workspace)
    {
        _workspace = workspace;
        ActiveSection = _remembered.TryGetValue(workspace, out var section)
            ? section
            : WorkspaceRailLayout.DefaultSection(workspace);
        OnPropertyChanged(nameof(LibraryOrder));
        OnPropertyChanged(nameof(HierarchyOrder));
        OnPropertyChanged(nameof(PdksOrder));
        OnPropertyChanged(nameof(GatesOrder));
        OnPropertyChanged(nameof(ChipletsOrder));
        OnPropertyChanged(nameof(IsLibraryAvailable));
        OnPropertyChanged(nameof(IsHierarchyAvailable));
        OnPropertyChanged(nameof(IsPdksAvailable));
        OnPropertyChanged(nameof(IsGatesAvailable));
        OnPropertyChanged(nameof(IsChipletsAvailable));
    }

    /// <summary>True when the active workspace's rail shows <paramref name="section"/>.</summary>
    public bool IsAvailable(LeftRailSection section) => WorkspaceRailLayout.Shows(_workspace, section);

    /// <summary>
    /// Shows <paramref name="section"/>; when the workspace leaves it out, first asks the host
    /// (<see cref="SectionUnavailable"/>) to switch to a workspace that shows it.
    /// </summary>
    public void Show(LeftRailSection section)
    {
        if (!IsAvailable(section))
            SectionUnavailable?.Invoke(section);
        if (IsAvailable(section))
            ActiveSection = section;
    }

    partial void OnActiveSectionChanged(LeftRailSection value) => _remembered[_workspace] = value;

    /// <summary>
    /// Switches to the section that hosts <paramref name="controlName"/> when a tour step
    /// targets a control inside the rail; any other target leaves the pane unchanged.
    /// </summary>
    /// <param name="controlName">The <c>x:Name</c> the tour spotlight anchors to.</param>
    public void RevealTourTarget(string? controlName)
    {
        if (controlName is null || !SectionByControlName.TryGetValue(controlName, out var section))
            return;
        Show(section);
    }

    private int OrderOf(LeftRailSection section)
    {
        var order = WorkspaceRailLayout.Order(_workspace);
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == section)
                return i;
        }
        return order.Count;
    }
}
