namespace CAP.Avalonia.ViewModels.Workspaces.Presentation;

/// <summary>
/// How a canvas overlay starts the first time a workspace is entered in a session. Afterwards
/// the workspace restores whatever the user left it at (<see cref="WorkspaceOverlayMemory"/>).
/// </summary>
public enum OverlayDefault
{
    /// <summary>The workspace leaves the overlay as it is.</summary>
    Keep = 0,

    /// <summary>The overlay is hidden.</summary>
    Off = 1,

    /// <summary>The overlay is shown.</summary>
    On = 2,

    /// <summary>
    /// The overlay is shown when there is something to show (a simulation result for the
    /// power overlay), otherwise hidden.
    /// </summary>
    OnWhenAvailable = 3,
}
