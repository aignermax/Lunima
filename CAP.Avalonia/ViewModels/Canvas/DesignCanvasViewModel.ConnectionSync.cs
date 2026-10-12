using CAP_Core.Components.Connections;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Keeps <see cref="DesignCanvasViewModel.Connections"/> (the view-model collection bound to
/// the canvas) and <see cref="DesignCanvasViewModel.ConnectionManager"/> (the core connection
/// list) in sync when a command restores a connection it previously removed — undo of a
/// delete, or undo of a Cut-tool split — or when the manager itself replaced connections (wires
/// merged through a deleted crossing). Idempotent, so undo/redo can call it freely.
/// </summary>
public partial class DesignCanvasViewModel
{
    /// <summary>
    /// Adds <paramref name="connection"/> back into the connection manager and, if no
    /// view-model already wraps it, creates one so the canvas renders it again.
    /// </summary>
    public void RestoreConnectionAndViewModel(WaveguideConnection connection)
    {
        ConnectionManager.AddExistingConnection(connection);
        if (!Connections.Any(c => c.Connection == connection))
            Connections.Add(new WaveguideConnectionViewModel(connection));
    }

    /// <summary>
    /// Puts deleted connections back (undo of a delete). A pin carries one wire, so a connection
    /// that took over one of their pins in the meantime — the merged wires of a deleted crossing —
    /// leaves first.
    /// </summary>
    public void RestoreConnections(IEnumerable<WaveguideConnection> connections)
    {
        var restored = connections.Distinct().ToList();
        var pins = restored.SelectMany(c => new[] { c.StartPin, c.EndPin }).ToHashSet();
        var displaced = ConnectionManager.Connections
            .Where(c => !restored.Contains(c) && (pins.Contains(c.StartPin) || pins.Contains(c.EndPin)))
            .ToList();
        foreach (var connection in displaced)
            ConnectionManager.RemoveConnectionDeferred(connection);
        foreach (var vm in Connections.Where(vm => displaced.Contains(vm.Connection)).ToList())
            Connections.Remove(vm);

        foreach (var connection in restored)
            RestoreConnectionAndViewModel(connection);
    }

    /// <summary>
    /// Mirrors the connection manager in <see cref="Connections"/>: removed wires disappear,
    /// wires the manager created (e.g. merged through a deleted crossing) appear.
    /// </summary>
    private void SyncConnectionViewModels()
    {
        var managed = ConnectionManager.Connections.ToHashSet();
        foreach (var stale in Connections.Where(vm => !managed.Contains(vm.Connection)).ToList())
            Connections.Remove(stale);
        var shown = Connections.Select(vm => vm.Connection).ToHashSet();
        foreach (var connection in managed.Where(c => !shown.Contains(c)))
            Connections.Add(new WaveguideConnectionViewModel(connection));
    }
}
