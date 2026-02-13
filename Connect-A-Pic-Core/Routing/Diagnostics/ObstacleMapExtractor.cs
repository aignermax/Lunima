using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Extracts obstacle map data around a region of interest from the pathfinding grid.
/// </summary>
public static class ObstacleMapExtractor
{
    /// <summary>
    /// Default padding in grid cells around the bounding box of the two endpoints.
    /// </summary>
    public const int DefaultPaddingCells = 10;

    /// <summary>
    /// Extracts blocked cells in the region between two physical positions (with padding).
    /// </summary>
    /// <param name="grid">The pathfinding grid to extract from.</param>
    /// <param name="startX">Start X in physical micrometers.</param>
    /// <param name="startY">Start Y in physical micrometers.</param>
    /// <param name="endX">End X in physical micrometers.</param>
    /// <param name="endY">End Y in physical micrometers.</param>
    /// <param name="paddingCells">Extra padding around the bounding box in grid cells.</param>
    /// <returns>List of blocked obstacle cells in the region.</returns>
    public static List<ObstacleCell> Extract(
        PathfindingGrid grid,
        double startX, double startY,
        double endX, double endY,
        int paddingCells = DefaultPaddingCells)
    {
        var (gx1, gy1) = grid.PhysicalToGrid(Math.Min(startX, endX), Math.Min(startY, endY));
        var (gx2, gy2) = grid.PhysicalToGrid(Math.Max(startX, endX), Math.Max(startY, endY));

        int minGx = Math.Max(0, gx1 - paddingCells);
        int minGy = Math.Max(0, gy1 - paddingCells);
        int maxGx = Math.Min(grid.Width - 1, gx2 + paddingCells);
        int maxGy = Math.Min(grid.Height - 1, gy2 + paddingCells);

        var cells = new List<ObstacleCell>();

        for (int gx = minGx; gx <= maxGx; gx++)
        {
            for (int gy = minGy; gy <= maxGy; gy++)
            {
                byte state = grid.GetCellState(gx, gy);
                if (state != 0)
                {
                    cells.Add(new ObstacleCell
                    {
                        GridX = gx,
                        GridY = gy,
                        State = state
                    });
                }
            }
        }

        return cells;
    }
}
