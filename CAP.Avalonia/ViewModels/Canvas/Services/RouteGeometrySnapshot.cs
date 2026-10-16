using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>
/// Geometry before one edit, not a grid or serialized design. Routing can change unrelated
/// wires too, so capture live routes once per gesture. The bounded command history owns
/// the snapshot; merged moves retain only the first one.
/// </summary>
public sealed class RouteGeometrySnapshot
{
    private readonly List<Entry> _entries;
    private RouteGeometrySnapshot(List<Entry> entries) => _entries = entries;

    public static RouteGeometrySnapshot Capture(DesignCanvasViewModel canvas)
    {
        lock (canvas.ConnectionManager.SyncRoot)
            return new(canvas.ConnectionManager.Connections.Select(c => new Entry(
                c, c.RoutedPath?.DeepCopy(), c.IsRouteFrozen, new(c.StraightShiftOffsets))).ToList());
    }

    /// <summary>Restore only identical connections present after topology undo.</summary>
    public void Restore(DesignCanvasViewModel canvas)
    {
        lock (canvas.ConnectionManager.SyncRoot)
        {
            var present = canvas.ConnectionManager.Connections.ToHashSet();
            foreach (var entry in _entries.Where(e => present.Contains(e.Connection)))
            {
                if (entry.Path is { } path) entry.Connection.RestoreCachedPath(path.DeepCopy());
                else entry.Connection.InvalidateRoute();
                entry.Connection.IsRouteFrozen = entry.Frozen;
                entry.Connection.StraightShiftOffsets.Clear();
                foreach (var pair in entry.Offsets) entry.Connection.StraightShiftOffsets.Add(pair.Key, pair.Value);
                var grid = canvas.Router.PathfindingGrid;
                grid?.RemoveWaveguideObstacle(entry.Connection.Id);
                if (entry.Connection.RoutedPath is { } restored)
                    grid?.AddWaveguideObstacle(entry.Connection.Id, restored.Segments,
                        canvas.ConnectionManager.ObstacleWidthFor(entry.Connection));
            }
        }
        foreach (var connection in canvas.Connections) connection.NotifyPathChanged();
    }

    private sealed record Entry(WaveguideConnection Connection, RoutedPath? Path,
        bool Frozen, Dictionary<int, double> Offsets);
}
