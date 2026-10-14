namespace CAP.Avalonia.ViewModels.Workspaces.Presentation;

/// <summary>
/// Canvas tools whose toolbar place depends on the workspace: in the primary toolbar, or one
/// click away in the "More tools" overflow. Select, Connect and Delete are always primary.
/// Keyboard shortcuts work for every tool in every workspace.
/// </summary>
public enum WorkspaceTool
{
    /// <summary>Probe mode: a waveguide's cross-section mode slice.</summary>
    Probe = 0,

    /// <summary>Cut mode (X): inserts a waveguide crossing.</summary>
    Cut = 1,
}
