using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Component-obstacle snapshot support for <see cref="PathfindingGrid"/> (split out to keep
/// the grid partials below the file-size limit).
/// </summary>
public partial class PathfindingGrid
{
    /// <summary>Copies primitive cell state for a quiescent pass-start snapshot; no grid object escapes.</summary>
    internal byte[] CaptureCellStates()
    {
        var cells = new byte[_cells.Length];
        Buffer.BlockCopy(_cells, 0, cells, 0, cells.Length);
        return cells;
    }

    /// <summary>
    /// The top-level components (and groups) currently registered as obstacles, taken under
    /// the bookkeeping lock. Rebuilding a fresh grid from exactly this set reproduces the
    /// component-side cell state of this grid — bodies, pin zones, corridors and frozen
    /// group path markings — without copying the mutable per-cell arrays.
    /// </summary>
    internal Component[] SnapshotObstacleComponents()
    {
        lock (_componentCellsLock)
        {
            return _componentCells.Keys.ToArray();
        }
    }

    /// <summary>
    /// Makes every cell that no waveguide occupies in <paramref name="source"/> read exactly
    /// as it does there. A grid rebuilt from the source's components only knows the
    /// component-side state; cells a caller set by hand (fenced foreign wires, exit lanes,
    /// opened gaps) live only in the source's cell array and would otherwise be lost.
    /// Waveguide cells keep this grid's own state — the state a cleared waveguide falls back
    /// to. Both grids must cover the same area with the same cell size.
    /// </summary>
    internal void CopyNonWaveguideCellStatesFrom(PathfindingGrid source)
    {
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("the source grid must have the same dimensions", nameof(source));
        bool changed = false;
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            byte state = source._cells[x, y];
            if (state == WaveguideCellState || _cells[x, y] == state)
                continue;
            _cells[x, y] = state;
            changed = true;
        }
        if (changed)
            WaveguideVersion++;
    }
}
