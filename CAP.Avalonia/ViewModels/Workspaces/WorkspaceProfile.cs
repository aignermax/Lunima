using CAP.Avalonia.ViewModels.Panels;

namespace CAP.Avalonia.ViewModels.Workspaces;

/// <summary>
/// What a <see cref="Workspace"/> means: the analysis-dock tabs it shows (the first one is
/// the tab Run lands on) and the localization keys of its Run button label and tooltip.
/// </summary>
/// <param name="Workspace">The workspace this profile describes.</param>
/// <param name="Tabs">Dock tabs shown in this workspace; never empty.</param>
/// <param name="RunLabelKey">String-table key of the Run button label.</param>
/// <param name="RunTipKey">String-table key of the Run button tooltip.</param>
/// <param name="LocksDieContent">
/// True when the unit of work is the die (System): the parts and wires on dies are dimmed and
/// locked, clicks select dies. False: the parts on dies are worked on directly.
/// </param>
public sealed record WorkspaceProfile(
    Workspace Workspace,
    IReadOnlyList<AnalysisDockTab> Tabs,
    string RunLabelKey,
    string RunTipKey,
    bool LocksDieContent = false)
{
    private static readonly WorkspaceProfile LayoutProfile = new(
        Workspace.Layout,
        [AnalysisDockTab.Checks],
        "Workspace.RunLayout", "Workspace.RunLayoutTip");

    private static readonly WorkspaceProfile OpticsProfile = new(
        Workspace.Optics,
        [
            AnalysisDockTab.Spectrum, AnalysisDockTab.MonteCarlo, AnalysisDockTab.Optimization,
            AnalysisDockTab.Sweep, AnalysisDockTab.Transient, AnalysisDockTab.EyeBer,
        ],
        "Workspace.RunOptics", "Workspace.RunOpticsTip");

    private static readonly WorkspaceProfile LogicProfile = new(
        Workspace.Logic,
        [AnalysisDockTab.Logic],
        "Workspace.RunLogic", "Workspace.RunLogicTip");

    private static readonly WorkspaceProfile SystemProfile = new(
        Workspace.System,
        [AnalysisDockTab.Checks, AnalysisDockTab.Logic, AnalysisDockTab.Spectrum],
        "Workspace.RunSystem", "Workspace.RunSystemTip",
        LocksDieContent: true);

    /// <summary>The profile of <paramref name="workspace"/>.</summary>
    public static WorkspaceProfile For(Workspace workspace) => workspace switch
    {
        Workspace.Optics => OpticsProfile,
        Workspace.Logic => LogicProfile,
        Workspace.System => SystemProfile,
        _ => LayoutProfile,
    };
}
