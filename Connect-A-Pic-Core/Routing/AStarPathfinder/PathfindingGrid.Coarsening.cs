namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Coarse-grid derivation for <see cref="PathfindingGrid"/> (issue #1418): a coarser copy of
/// the current obstacle state so the router can retry a flooded A* search on a smaller grid.
/// </summary>
public partial class PathfindingGrid
{
    /// <summary>
    /// Builds a throwaway copy of this grid whose cells are <paramref name="factor"/> times
    /// larger, aligned to the same physical origin. A coarse cell is blocked when ANY fine
    /// cell inside it is blocked (the highest cell state wins: frozen path &gt; waveguide &gt;
    /// component &gt; free), so the coarse grid never opens a passage the fine grid lacks —
    /// coarse detours can only cut across regions that are genuinely free, and the result is
    /// re-validated on this fine grid before acceptance. Soft pin-zone penalties are not
    /// copied; the coarse search is a last-resort reachability probe, not a cost-optimal one.
    /// </summary>
    /// <param name="factor">Cell-size multiplier (2 = half resolution per axis).</param>
    public PathfindingGrid CreateCoarseCopy(int factor)
    {
        if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor), "Factor must be >= 1.");

        var coarse = new PathfindingGrid(
            MinX, MinY, MaxX, MaxY, CellSizeMicrometers * factor, ObstaclePaddingMicrometers);

        for (int cx = 0; cx < coarse.Width; cx++)
        {
            for (int cy = 0; cy < coarse.Height; cy++)
            {
                byte state = MostBlockedStateInBlock(cx * factor, cy * factor, factor);
                if (state != 0)
                    coarse.SetCellState(cx, cy, state);
            }
        }
        return coarse;
    }

    /// <summary>The highest cell state among the fine cells in the block starting at (x0, y0).</summary>
    private byte MostBlockedStateInBlock(int x0, int y0, int factor)
    {
        byte state = 0;
        int xEnd = Math.Min(x0 + factor, Width);
        int yEnd = Math.Min(y0 + factor, Height);
        for (int x = x0; x < xEnd && state < 3; x++)
        {
            for (int y = y0; y < yEnd; y++)
            {
                byte cellState = GetCellState(x, y);
                if (cellState > state) state = cellState;
            }
        }
        return state;
    }
}
