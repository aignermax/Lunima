using System.Collections.Generic;
using CAP.Avalonia.ViewModels.Workspaces;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>
/// How the left rail arranges itself per workspace: which section opens by default and the
/// order of the rail icons. Every workspace lists every section — only the default and the
/// order change, so no panel ever becomes unreachable. Layout puts the fabrication context
/// (PDKs and their process) right under the library.
/// </summary>
public static class WorkspaceRailLayout
{
    private static readonly IReadOnlyList<LeftRailSection> LayoutOrder =
        [LeftRailSection.Library, LeftRailSection.Pdks, LeftRailSection.Hierarchy, LeftRailSection.Gates, LeftRailSection.Chiplets];

    private static readonly IReadOnlyList<LeftRailSection> OpticsOrder =
        [LeftRailSection.Library, LeftRailSection.Hierarchy, LeftRailSection.Pdks, LeftRailSection.Gates, LeftRailSection.Chiplets];

    private static readonly IReadOnlyList<LeftRailSection> LogicOrder =
        [LeftRailSection.Gates, LeftRailSection.Library, LeftRailSection.Hierarchy, LeftRailSection.Pdks, LeftRailSection.Chiplets];

    private static readonly IReadOnlyList<LeftRailSection> SystemOrder =
        [LeftRailSection.Chiplets, LeftRailSection.Library, LeftRailSection.Hierarchy, LeftRailSection.Pdks, LeftRailSection.Gates];

    /// <summary>The rail icons of <paramref name="workspace"/>, top to bottom; always all sections.</summary>
    public static IReadOnlyList<LeftRailSection> Order(Workspace workspace) => workspace switch
    {
        Workspace.Optics => OpticsOrder,
        Workspace.Logic => LogicOrder,
        Workspace.System => SystemOrder,
        _ => LayoutOrder,
    };

    /// <summary>The section a workspace opens on until the user picks another one: its first icon.</summary>
    public static LeftRailSection DefaultSection(Workspace workspace) => Order(workspace)[0];
}
