namespace CAP.Avalonia.ViewModels.Workspaces.Presentation;

/// <summary>
/// Remembers the overlays the user left each workspace with, for the session: switching
/// back restores them. A workspace not visited yet starts from its
/// <see cref="WorkspacePresentation"/> defaults.
/// </summary>
public sealed class WorkspaceOverlayMemory
{
    private readonly Dictionary<Workspace, WorkspaceOverlayState> _remembered = new();

    /// <summary>Records the overlays <paramref name="workspace"/> is left with.</summary>
    public void Remember(Workspace workspace, WorkspaceOverlayState state) => _remembered[workspace] = state;

    /// <summary>True when <paramref name="workspace"/> has been left at least once this session.</summary>
    public bool HasRemembered(Workspace workspace) => _remembered.ContainsKey(workspace);

    /// <summary>
    /// The overlays to show on entering <paramref name="workspace"/>: an overlay the
    /// workspace keeps stays as it is; otherwise the remembered state, or the default.
    /// </summary>
    /// <param name="workspace">The workspace being entered.</param>
    /// <param name="presentation">Its presentation (overlay defaults).</param>
    /// <param name="current">The overlays on screen right now.</param>
    /// <param name="hasSimulationResult">Whether a simulation result exists to show.</param>
    public WorkspaceOverlayState Resolve(
        Workspace workspace,
        WorkspacePresentation presentation,
        WorkspaceOverlayState current,
        bool hasSimulationResult)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        bool hasMemory = _remembered.TryGetValue(workspace, out var remembered);
        return new WorkspaceOverlayState(
            PowerFlow: Pick(presentation.PowerFlow, current.PowerFlow,
                hasMemory ? remembered.PowerFlow : null, hasSimulationResult),
            DesignCheckMarkers: Pick(presentation.DesignCheckMarkers, current.DesignCheckMarkers,
                hasMemory ? remembered.DesignCheckMarkers : null, isAvailable: true));
    }

    private static bool Pick(OverlayDefault policy, bool current, bool? remembered, bool isAvailable) => policy switch
    {
        OverlayDefault.Keep => current,
        _ when remembered.HasValue => remembered.Value,
        OverlayDefault.On => true,
        OverlayDefault.OnWhenAvailable => isAvailable,
        _ => false,
    };
}
