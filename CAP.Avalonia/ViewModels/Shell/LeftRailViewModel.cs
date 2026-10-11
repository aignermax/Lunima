using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>
/// State of the main window's left icon rail: which section fills the one full-height pane
/// next to it (VS Code / Figma style). The library is the default because placing
/// components is the first thing every workflow does.
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

    /// <summary>The section currently shown in the left pane.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryActive), nameof(IsHierarchyActive), nameof(IsPdksActive))]
    private LeftRailSection _activeSection = LeftRailSection.Library;

    /// <summary>True while the component library is shown.</summary>
    public bool IsLibraryActive => ActiveSection == LeftRailSection.Library;

    /// <summary>True while the design hierarchy is shown.</summary>
    public bool IsHierarchyActive => ActiveSection == LeftRailSection.Hierarchy;

    /// <summary>True while PDK management is shown.</summary>
    public bool IsPdksActive => ActiveSection == LeftRailSection.Pdks;

    /// <summary>Shows the component library.</summary>
    [RelayCommand]
    private void ShowLibrary() => ActiveSection = LeftRailSection.Library;

    /// <summary>Shows the design hierarchy.</summary>
    [RelayCommand]
    private void ShowHierarchy() => ActiveSection = LeftRailSection.Hierarchy;

    /// <summary>Shows PDK management.</summary>
    [RelayCommand]
    private void ShowPdks() => ActiveSection = LeftRailSection.Pdks;

    /// <summary>
    /// Switches to the section that hosts <paramref name="controlName"/> when a tour step
    /// targets a control inside the rail; any other target leaves the pane unchanged.
    /// </summary>
    /// <param name="controlName">The <c>x:Name</c> the tour spotlight anchors to.</param>
    public void RevealTourTarget(string? controlName)
    {
        if (controlName is null || !SectionByControlName.TryGetValue(controlName, out var section))
            return;
        ActiveSection = section;
    }
}
