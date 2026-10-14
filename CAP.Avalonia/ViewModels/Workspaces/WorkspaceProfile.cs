using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces.Presentation;
using CAP.Avalonia.ViewModels.Workspaces.ToolSets;

namespace CAP.Avalonia.ViewModels.Workspaces;

/// <summary>
/// What a <see cref="Workspace"/> means: the analysis-dock tabs it shows (the first one is
/// the tab the dock opens on), the localization keys of its Run button label and tooltip,
/// and how it presents the design (<see cref="WorkspacePresentation"/>: overlays, canvas
/// emphasis, toolbar placement) and its tool set (<see cref="WorkspaceToolSet"/>: what it places,
/// its left-rail sections, the component editor).
/// </summary>
/// <param name="Workspace">The workspace this profile describes.</param>
/// <param name="Tabs">Dock tabs shown in this workspace; never empty.</param>
/// <param name="RunLabelKey">String-table key of the Run button label.</param>
/// <param name="RunTipKey">String-table key of the Run button tooltip.</param>
/// <param name="Presentation">Overlay defaults, canvas emphasis and toolbar placement.</param>
/// <param name="ToolSet">Library scope, rail sections and editors of the workspace.</param>
public sealed record WorkspaceProfile(
    Workspace Workspace,
    IReadOnlyList<AnalysisDockTab> Tabs,
    string RunLabelKey,
    string RunTipKey,
    WorkspacePresentation Presentation,
    WorkspaceToolSet ToolSet)
{
    private static readonly WorkspaceProfile LayoutProfile = new(
        Workspace.Layout,
        [AnalysisDockTab.Checks],
        "Workspace.RunLayout", "Workspace.RunLayoutTip",
        WorkspacePresentation.Layout, WorkspaceToolSet.Layout);

    private static readonly WorkspaceProfile OpticsProfile = new(
        Workspace.Optics,
        [
            AnalysisDockTab.Transient, AnalysisDockTab.Sweep, AnalysisDockTab.EyeBer,
            AnalysisDockTab.Spectrum, AnalysisDockTab.MonteCarlo, AnalysisDockTab.Optimization,
        ],
        "Workspace.RunOptics", "Workspace.RunOpticsTip",
        WorkspacePresentation.Optics, WorkspaceToolSet.Optics);

    private static readonly WorkspaceProfile LogicProfile = new(
        Workspace.Logic,
        [AnalysisDockTab.Logic],
        "Workspace.RunLogic", "Workspace.RunLogicTip",
        WorkspacePresentation.Neutral, WorkspaceToolSet.Logic);

    private static readonly WorkspaceProfile SystemProfile = new(
        Workspace.System,
        [AnalysisDockTab.Checks, AnalysisDockTab.Logic, AnalysisDockTab.Spectrum],
        "Workspace.RunSystem", "Workspace.RunSystemTip",
        WorkspacePresentation.Neutral, WorkspaceToolSet.System);

    /// <summary>The profile of <paramref name="workspace"/>.</summary>
    public static WorkspaceProfile For(Workspace workspace) => workspace switch
    {
        Workspace.Optics => OpticsProfile,
        Workspace.Logic => LogicProfile,
        Workspace.System => SystemProfile,
        _ => LayoutProfile,
    };
}
