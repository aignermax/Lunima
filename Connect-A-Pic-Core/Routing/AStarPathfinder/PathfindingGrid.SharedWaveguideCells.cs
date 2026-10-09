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
