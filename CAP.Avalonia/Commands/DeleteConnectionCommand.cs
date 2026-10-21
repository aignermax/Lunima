using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for deleting a waveguide connection. Crossing-aware (#705): deleting a
/// crossing sub-connection dissolves the crossing (the manager restores the other
/// net unsplit), and undo restores the pre-split ORIGINAL connection — never a
/// sub-connection whose crossing component no longer exists.
/// </summary>
public class DeleteConnectionCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;

    /// <summary>The connection to remove on Execute and restore on Undo.</summary>
    private WaveguideConnection _target;
    private bool _removed;

    public DeleteConnectionCommand(DesignCanvasViewModel canvas, WaveguideConnectionViewModel connectionVm)
    {
        _canvas = canvas;
        _target = connectionVm.Connection;
    }

    public string Description => "Delete connection";

    public void Execute()
    {
        _removed = false;
        // Don't delete locked connections
        if (_target.IsLocked)
            return;

        var crossing = _canvas.ConnectionManager.CrossingService;
        var targetVm = _canvas.Connections.FirstOrDefault(c => c.Connection == _target);

        // Only act if the connection still exists somewhere (canvas VM, manager,
        // or as part of a crossing) — supports redo after intervening changes.
        bool exists = targetVm != null
            || _canvas.ConnectionManager.Connections.Contains(_target)
            || crossing?.IsCrossingConnection(_target) == true;
        if (!exists)
            return;

        // A piece of a routing-placed crossing chain stands for its whole wire: the chain
        // goes first, so no crossing is left behind and undo restores a whole wire.
        if (ChainCrossingMaintenance.DocksOnTemporaryCrossing(_target)
            && _canvas.DissolveInsertedCrossingChainOf(_target) is { } wire)
        {
            _target = wire;
            targetVm = _canvas.Connections.FirstOrDefault(c => c.Connection == _target);
        }

        // Undo must restore the pre-split original, not a crossing sub-connection.
        var restoreTarget = crossing?.ResolveToOriginal(_target) ?? _target;

        if (targetVm != null)
            _canvas.Connections.Remove(targetVm);

        // Dissolves the crossing when _target participates in one (the canvas
        // binder syncs the remaining connection view-models).
        _canvas.ConnectionManager.RemoveConnectionDeferred(_target);
        _target = restoreTarget;
        _removed = true;

        _ = _canvas.RecalculateRoutesAsync();
    }

    public void Undo()
    {
        // A preceding component/crossing deletion in a mixed batch may already have
        // removed this segment. A no-op must not resurrect it with orphaned endpoints.
        if (!_removed) return;
        _canvas.RestoreConnectionAndViewModel(_target);
        _ = _canvas.RecalculateRoutesAsync();
    }
}
