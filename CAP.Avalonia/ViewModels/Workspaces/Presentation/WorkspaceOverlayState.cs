namespace CAP.Avalonia.ViewModels.Workspaces.Presentation;

/// <summary>The canvas overlays a workspace shows.</summary>
/// <param name="PowerFlow">The CW simulation and its power-flow overlay are on.</param>
/// <param name="DesignCheckMarkers">The design-check findings are marked on the canvas.</param>
public readonly record struct WorkspaceOverlayState(bool PowerFlow, bool DesignCheckMarkers);
