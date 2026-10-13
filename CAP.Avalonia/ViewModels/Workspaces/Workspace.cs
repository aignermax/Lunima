namespace CAP.Avalonia.ViewModels.Workspaces;

/// <summary>
/// A task-focused view of the editor: what Run does and which analysis-dock tabs are
/// shown. Switching workspace never changes the design, only the tools around it.
/// </summary>
public enum Workspace
{
    /// <summary>Placing and routing; Run checks the layout (DRC-lite).</summary>
    Layout = 0,

    /// <summary>Optical simulation; Run simulates (CW or transient).</summary>
    Optics = 1,

    /// <summary>Photonic logic; Run builds the logic network, then steps the clock.</summary>
    Logic = 2,

    /// <summary>Multi-chiplet assembly; Run checks the chiplet links and simulates the whole system.</summary>
    System = 3,
}
