using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces.ContextPanels;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.ViewModels.Workspaces.ToolSets;

/// <summary>
/// The tools a workspace offers on its own object model: what its left panel lets the user
/// place (<see cref="Library"/>), which left-rail sections it shows (in order; the first opens
/// by default) and whether the component editor applies. Pure data, read from the
/// <see cref="WorkspaceProfile"/>. Layout keeps every section, so a section another workspace
/// leaves out is always one click away through "switch to Layout".
/// </summary>
/// <param name="Library">What the workspace places from its left panel.</param>
/// <param name="RailSections">Left-rail sections shown, top to bottom; never empty.</param>
/// <param name="AllowsComponentEditor">True where "Edit Component" (S-matrix / component editor) is offered.</param>
public sealed record WorkspaceToolSet(
    LibraryScope Library,
    IReadOnlyList<LeftRailSection> RailSections,
    bool AllowsComponentEditor)
{
    /// <summary>Layout: the full library, every rail section, every editor.</summary>
    public static readonly WorkspaceToolSet Layout = new(
        LibraryScope.AllComponents,
        [LeftRailSection.Library, LeftRailSection.Pdks, LeftRailSection.Hierarchy, LeftRailSection.Gates, LeftRailSection.Chiplets],
        AllowsComponentEditor: true);

    /// <summary>Optics: instruments only; parameters stay editable in the component editor.</summary>
    public static readonly WorkspaceToolSet Optics = new(
        LibraryScope.Instruments,
        [LeftRailSection.Library, LeftRailSection.Hierarchy, LeftRailSection.Pdks],
        AllowsComponentEditor: true);

    /// <summary>Logic: the gate library instead of the component library.</summary>
    public static readonly WorkspaceToolSet Logic = new(
        LibraryScope.Gates,
        [LeftRailSection.Gates, LeftRailSection.Hierarchy],
        AllowsComponentEditor: false);

    /// <summary>System: no library — the Chiplets panel is the left pane.</summary>
    public static readonly WorkspaceToolSet System = new(
        LibraryScope.None,
        [LeftRailSection.Chiplets, LeftRailSection.Hierarchy, LeftRailSection.Pdks],
        AllowsComponentEditor: false);

    /// <summary>True when the rail shows <paramref name="section"/>.</summary>
    public bool Shows(LeftRailSection section) => RailSections.Contains(section);

    /// <summary>True when the library list offers <paramref name="template"/>.</summary>
    public bool CanPlace(ComponentTemplate template) => Library switch
    {
        LibraryScope.AllComponents => true,
        LibraryScope.Instruments => InstrumentTemplates.IsInstrument(template),
        _ => false,
    };

    /// <summary>True when a saved group / PDK macro / gate may be placed.</summary>
    public bool CanPlace(GroupTemplate template) => Library switch
    {
        LibraryScope.AllComponents => true,
        LibraryScope.Gates => GatesPanelViewModel.IsGate(template),
        _ => false,
    };
}
