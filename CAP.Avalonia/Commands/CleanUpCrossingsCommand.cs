using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Commands;

/// <summary>
/// "Clean up crossings": removes every crossing routing placed, merges the wires through
/// them pin to pin and lets the next routing pass route them again (with new crossings only
/// where adaptive insertion is on and a wire needs one). Crossings the user placed stay.
/// Undo puts the crossings, their pieces and every route back exactly as they were.
/// </summary>
public sealed class CleanUpCrossingsCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private List<(WaveguideConnection Connection, RoutedPath? Path, bool Frozen)> _connectionsBefore = new();
    private List<(Component Crossing, string? TemplateName, string? TemplatePdkSource)> _removedCrossings = new();
    private HashSet<Component> _crossingsBefore = new();

    /// <summary>Creates the command for the given canvas.</summary>
    public CleanUpCrossingsCommand(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
    }

    /// <inheritdoc />
    public string Description => "Clean up crossings";

    /// <summary>Number of crossings the last execution removed.</summary>
    public int RemovedCount => _removedCrossings.Count;

    /// <inheritdoc />
    public void Execute()
    {
        var manager = _canvas.ConnectionManager;
        _connectionsBefore = CrossingCanvasSync.SnapshotConnections(_canvas)
            .Select(c => (c, c.RoutedPath?.DeepCopy(), c.IsRouteFrozen))
            .ToList();
        var crossingVms = _canvas.Components
            .Where(vm => ChainCrossingMaintenance.IsTemporaryCrossing(vm.Component))
            .ToDictionary(vm => vm.Component);
        _crossingsBefore = crossingVms.Keys.ToHashSet();

        var removed = manager.RemoveAllInsertedCrossings();
        _removedCrossings = removed
            .Select(c => crossingVms.TryGetValue(c, out var vm)
                ? (c, vm.TemplateName, vm.TemplatePdkSource)
                : (c, (string?)null, (string?)null))
            .ToList();
        _canvas.ApplyCrossingRemoval(removed);
        if (removed.Count > 0)
            _ = _canvas.RecalculateRoutesAsync();
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_removedCrossings.Count == 0) return;
        RestoreConnections();
        // Crossings the re-route placed after the clean-up belong to wires that are back in
        // their old shape now.
        foreach (var placedSince in _canvas.Components
                     .Where(vm => ChainCrossingMaintenance.IsTemporaryCrossing(vm.Component) && !_crossingsBefore.Contains(vm.Component))
                     .Select(vm => vm.Component).ToList())
            CrossingCanvasSync.RemoveCrossing(_canvas, placedSince);
        foreach (var (crossing, templateName, pdkSource) in _removedCrossings)
            CrossingCanvasSync.AddCrossing(_canvas, crossing, templateName, pdkSource);
        CrossingCanvasSync.SyncConnections(_canvas, CrossingCanvasSync.SnapshotConnections(_canvas));

        var manager = _canvas.ConnectionManager;
        if (manager.CrossingService is { } service)
        {
            service.Reset();
            CrossingRecordRebuilder.Rebuild(service, manager, _canvas.Components.Select(vm => vm.Component));
        }
        _ = _canvas.RecalculateRoutesAsync();
    }

    /// <summary>Puts the connection list and every route back to the state before Execute.</summary>
    private void RestoreConnections()
    {
        var manager = _canvas.ConnectionManager;
        var grid = _canvas.Router.PathfindingGrid;
        lock (manager.SyncRoot)
        {
            foreach (var connection in manager.Connections)
                grid?.RemoveWaveguideObstacle(connection.Id);
            manager.Connections.Clear();
            foreach (var (connection, path, frozen) in _connectionsBefore)
            {
                if (path != null)
                    connection.RestoreCachedPath(path.DeepCopy());
                else
                    connection.InvalidateRoute();
                connection.IsRouteFrozen = frozen;
                manager.Connections.Add(connection);
            }
        }
    }
}
