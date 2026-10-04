using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Component-obstacle snapshot support for <see cref="PathfindingGrid"/> (split out to keep
/// the grid partials below the file-size limit).
/// </summary>
public partial class PathfindingGrid
{
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
}
