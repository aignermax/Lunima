namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Shared-cell bookkeeping for <see cref="PathfindingGrid"/>: two waveguides can cover the
/// same cell (a crossing, a blocked fallback line drawn across another wire). Removing one
/// of them must keep the cells the other still covers blocked — otherwise the removal
/// punches a hole into the remaining wire that later routes slip through.
/// </summary>
public partial class PathfindingGrid
{
    // Cell bounding box per waveguide, so a removal only intersects with wires that can overlap.
    private readonly Dictionary<Guid, (int MinX, int MinY, int MaxX, int MaxY)> _waveguideCellBounds = new();
    private readonly Dictionary<Guid, WaveguideObstacleShape> _waveguideShapes = new();

    /// <summary>Restores exact obstacle geometry, reusing its raster when shape and width match.</summary>
    public void RestoreWaveguideObstacle(Guid connectionId, IReadOnlyList<PathSegment> segments, double waveguideWidth)
    {
        HashSet<(int, int)>? repaired = null;
        var reused = false;
        lock (_waveguideCellsLock)
        {
            if (_waveguideShapes.TryGetValue(connectionId, out var shape) && shape.Matches(segments, waveguideWidth))
            {
                reused = true;
                _waveguideGeometry[connectionId] = segments.ToList();
                // Moving a component can clear cells underneath an unchanged wire.
                // Repair those cells, while retaining component and group ownership.
                foreach (var cell in _waveguideCells[connectionId])
                    if (IsInBounds(cell.x, cell.y) && _cells[cell.x, cell.y] == 0)
                    {
                        _cells[cell.x, cell.y] = 2;
                        (repaired ??= new()).Add(cell);
                    }
            }
        }
        if (!reused)
            AddWaveguideObstacle(connectionId, segments, waveguideWidth);
        else if (repaired != null)
        {
            WaveguideVersion++;
            OnWaveguideCellsAdded?.Invoke(repaired);
        }
    }

    /// <summary>
    /// Restores a history snapshot without rasterizing unchanged routes. Changed routes
    /// leave the grid in one ownership pass, preserving wires outside the snapshot.
    /// </summary>
    public void RestoreWaveguideObstacles(IEnumerable<(Guid Id, IReadOnlyList<PathSegment>? Segments, double Width)> obstacles)
    {
        var restoring = obstacles.ToArray();
        Guid[] changed;
        lock (_waveguideCellsLock)
            changed = restoring.Where(entry => entry.Segments == null ||
                !_waveguideShapes.TryGetValue(entry.Id, out var shape) ||
                !shape.Matches(entry.Segments, entry.Width)).Select(entry => entry.Id).ToArray();
        RemoveWaveguideObstacles(changed);
        foreach (var entry in restoring)
            if (entry.Segments != null)
                RestoreWaveguideObstacle(entry.Id, entry.Segments, entry.Width);
    }

    /// <summary>
    /// Removes a set of routes in one ownership pass. Geometry restoration replaces many
    /// routes together; checking every removed route against all the others is quadratic.
    /// Obstacles belonging to connections outside the batch remain registered and blocked.
    /// </summary>
    public void RemoveWaveguideObstacles(IEnumerable<Guid> connectionIds)
    {
        var removedCells = new HashSet<(int x, int y)>();
        HashSet<(int x, int y)> stillCovered;
        var removedAny = false;
        lock (_waveguideCellsLock)
        {
            foreach (var id in connectionIds)
            {
                if (!_waveguideCells.Remove(id, out var cells)) continue;
                removedAny = true;
                removedCells.UnionWith(cells);
                _waveguideEndpoints.Remove(id);
                _waveguideGeometry.Remove(id);
                _waveguideShapes.Remove(id);
                _waveguideHalfWidths.Remove(id);
                _waveguideCellBounds.Remove(id);
            }
            if (!removedAny) return;
            stillCovered = CellsStillCoveredByOtherWaveguides(removedCells);
        }
        foreach (var (gx, gy) in removedCells)
            if (IsInBounds(gx, gy) && _cells[gx, gy] == 2 && !stillCovered.Contains((gx, gy)))
                _cells[gx, gy] = FreedCellState(gx, gy);
        WaveguideVersion++;
    }

    /// <summary>Records the bounding box of a waveguide's cells. Call under the waveguide lock.</summary>
    private void RecordWaveguideCellBounds(Guid connectionId, HashSet<(int x, int y)> cells)
    {
        if (cells.Count == 0)
        {
            _waveguideCellBounds.Remove(connectionId);
            return;
        }
        _waveguideCellBounds[connectionId] = (cells.Min(c => c.x), cells.Min(c => c.y), cells.Max(c => c.x), cells.Max(c => c.y));
    }

    /// <summary>
    /// The cells of a just-removed waveguide that another registered waveguide still covers.
    /// Call under the waveguide lock, after the removed wire left the bookkeeping.
    /// </summary>
    private HashSet<(int x, int y)> CellsStillCoveredByOtherWaveguides(HashSet<(int x, int y)> removedCells)
    {
        var kept = new HashSet<(int x, int y)>();
        if (removedCells.Count == 0) return kept;
        var removed = (MinX: removedCells.Min(c => c.x), MinY: removedCells.Min(c => c.y),
                       MaxX: removedCells.Max(c => c.x), MaxY: removedCells.Max(c => c.y));
        foreach (var (owner, bounds) in _waveguideCellBounds)
        {
            if (bounds.MaxX < removed.MinX || bounds.MinX > removed.MaxX || bounds.MaxY < removed.MinY || bounds.MinY > removed.MaxY)
                continue;
            var other = _waveguideCells[owner];
            var (smaller, larger) = other.Count < removedCells.Count ? (other, removedCells) : (removedCells, other);
            foreach (var cell in smaller)
                if (larger.Contains(cell))
                    kept.Add(cell);
        }
        return kept;
    }
}
