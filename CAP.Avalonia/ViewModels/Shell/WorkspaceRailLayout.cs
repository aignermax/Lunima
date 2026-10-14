using System.Collections.Generic;
using CAP.Avalonia.ViewModels.Workspaces;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>
/// How the left rail arranges itself per workspace: which sections it shows, in which order,
/// and which one opens by default — read from the workspace's tool set
/// (<see cref="Workspaces.ToolSets.WorkspaceToolSet.RailSections"/>). Layout shows every
/// section; a section another workspace leaves out stays one click away through "switch to
/// Layout". Layout puts the fabrication context (PDKs and their process) right under the library.
/// </summary>
public static class WorkspaceRailLayout
{
    /// <summary>The rail icons of <paramref name="workspace"/>, top to bottom.</summary>
    public static IReadOnlyList<LeftRailSection> Order(Workspace workspace) =>
        WorkspaceProfile.For(workspace).ToolSet.RailSections;

    /// <summary>True when the rail of <paramref name="workspace"/> shows <paramref name="section"/>.</summary>
    public static bool Shows(Workspace workspace, LeftRailSection section) =>
        WorkspaceProfile.For(workspace).ToolSet.Shows(section);

    /// <summary>The section a workspace opens on until the user picks another one: its first icon.</summary>
    public static LeftRailSection DefaultSection(Workspace workspace) => Order(workspace)[0];
}
