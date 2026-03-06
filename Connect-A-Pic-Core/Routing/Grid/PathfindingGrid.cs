using CAP_Core.Routing.Grid;
using CAP_Core.Components;

namespace CAP_Core.Routing.Grid;

/// <summary>
/// Manages the pathfinding grid for A* waveguide routing.
/// Handles coordinate conversion between physical micrometers and grid cells,
/// and tracks which cells are blocked by component obstacles.
/// </summary>
public class PathfindingGrid
{
    public double CellSizeMicrometers { get; }
    public double ObstaclePaddingMicrometers { get; set; }

    public double MinX { get; private set; }
    public double MinY { get; private set; }
    public double MaxX { get; private set; }
    public double MaxY { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }

    // Cell states: 0 = free, 1 = blocked by component, 2 = blocked by waveguide
    private byte[,] _cells;

    /// <summary>
    /// Manages component/waveguide obstacle tracking and pin reservation zones.
    /// </summary>
    public GridObstacleManager ObstacleManager { get; }

    /// <summary>
    /// Creates a grid covering the specified area.
    /// </summary>
    public PathfindingGrid(double minX, double minY, double maxX, double maxY,
                           double cellSize = 1.0, double padding = 1.0)
    {
        CellSizeMicrometers = cellSize;
        ObstaclePaddingMicrometers = padding;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;

        Width = Math.Max((int)Math.Ceiling((maxX - minX) / cellSize), 1);
        Height = Math.Max((int)Math.Ceiling((maxY - minY) / cellSize), 1);

        _cells = new byte[Width, Height];
        ObstacleManager = new GridObstacleManager(this);
    }

    // --- Coordinate conversion ---

    public (int gridX, int gridY) PhysicalToGrid(double physicalX, double physicalY)
    {
        int gridX = (int)Math.Floor((physicalX - MinX) / CellSizeMicrometers);
        int gridY = (int)Math.Floor((physicalY - MinY) / CellSizeMicrometers);
        return (Math.Clamp(gridX, 0, Width - 1), Math.Clamp(gridY, 0, Height - 1));
    }

    public (double x, double y) GridToPhysical(int gridX, int gridY)
    {
        double x = MinX + (gridX + 0.5) * CellSizeMicrometers;
        double y = MinY + (gridY + 0.5) * CellSizeMicrometers;
        return (x, y);
    }

    // --- Cell state access ---

    public bool IsBlocked(int gridX, int gridY) =>
        !IsInBounds(gridX, gridY) || _cells[gridX, gridY] != 0;

    public byte GetCellState(int gridX, int gridY) =>
        IsInBounds(gridX, gridY) ? _cells[gridX, gridY] : (byte)1;

    public void SetCellState(int gridX, int gridY, byte state)
    {
        if (IsInBounds(gridX, gridY))
            _cells[gridX, gridY] = state;
    }

    public bool IsInBounds(int gridX, int gridY) =>
        gridX >= 0 && gridX < Width && gridY >= 0 && gridY < Height;

    public bool IsPinReservationZone(int gridX, int gridY) =>
        ObstacleManager.IsPinReservationZone(gridX, gridY);

    // --- Obstacle delegation ---

    public void AddComponentObstacle(Component component) =>
        ObstacleManager.AddComponentObstacle(component);

    public void RemoveComponentObstacle(Component component) =>
        ObstacleManager.RemoveComponentObstacle(component);

    public void UpdateComponentObstacle(Component component) =>
        ObstacleManager.UpdateComponentObstacle(component);

    public void AddWaveguideObstacle(Guid connectionId, IEnumerable<PathSegment> segments,
                                      double waveguideWidth) =>
        ObstacleManager.AddWaveguideObstacle(connectionId, segments, waveguideWidth);

    public void RemoveWaveguideObstacle(Guid connectionId) =>
        ObstacleManager.RemoveWaveguideObstacle(connectionId);

    public void ClearAllWaveguideObstacles() =>
        ObstacleManager.ClearAllWaveguideObstacles();

    // --- Grid operations ---

    public void RebuildFromComponents(IEnumerable<Component> components)
    {
        Array.Clear(_cells);
        ObstacleManager.Clear();

        foreach (var component in components)
            AddComponentObstacle(component);
    }

    /// <summary>
    /// Temporarily clears cells in a rectangular area.
    /// Returns the cells that were cleared so they can be restored.
    /// </summary>
    public Dictionary<(int x, int y), byte> ClearArea(double physX, double physY,
                                                       double width, double height)
    {
        var clearedCells = new Dictionary<(int, int), byte>();
        var (gx1, gy1) = PhysicalToGrid(physX, physY);
        var (gx2, gy2) = PhysicalToGrid(physX + width, physY + height);

        for (int gx = gx1; gx <= gx2; gx++)
        {
            for (int gy = gy1; gy <= gy2; gy++)
            {
                if (IsInBounds(gx, gy) && _cells[gx, gy] != 0)
                {
                    clearedCells[(gx, gy)] = _cells[gx, gy];
                    _cells[gx, gy] = 0;
                }
            }
        }
        return clearedCells;
    }

    public void RestoreCells(Dictionary<(int x, int y), byte> cells)
    {
        foreach (var ((gx, gy), state) in cells)
        {
            if (IsInBounds(gx, gy))
                _cells[gx, gy] = state;
        }
    }

    /// <summary>
    /// Clears a corridor from a pin position in its direction.
    /// Only clears component obstacles (state=1), NOT waveguide obstacles (state=2).
    /// </summary>
    public Dictionary<(int x, int y), byte> ClearPinCorridor(
        double pinX, double pinY, double angleDegrees,
        double corridorLength, double corridorWidth)
    {
        var clearedCells = new Dictionary<(int, int), byte>();

        double angleRad = angleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(angleRad);
        double dy = Math.Sin(angleRad);
        double perpX = -dy;
        double perpY = dx;

        for (double dist = 0; dist <= corridorLength; dist += CellSizeMicrometers)
        {
            double centerX = pinX + dx * dist;
            double centerY = pinY + dy * dist;

            for (double offset = -corridorWidth / 2; offset <= corridorWidth / 2;
                 offset += CellSizeMicrometers)
            {
                double cellX = centerX + perpX * offset;
                double cellY = centerY + perpY * offset;
                var (gx, gy) = PhysicalToGrid(cellX, cellY);

                if (IsInBounds(gx, gy) && _cells[gx, gy] == 1)
                {
                    clearedCells[(gx, gy)] = _cells[gx, gy];
                    _cells[gx, gy] = 0;
                }
            }
        }
        return clearedCells;
    }

    public int GetBlockedCellCount()
    {
        int count = 0;
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                if (_cells[x, y] != 0) count++;
        return count;
    }

    /// <summary>
    /// Marks a circular zone around a pin position as a reservation zone.
    /// Only marks cells that are currently free (state=0) — doesn't mark obstacles.
    /// </summary>
    /// <summary>
    /// Marks cells along a line with given half-width.
    /// </summary>
    private void MarkLineAsCells(double x1, double y1, double x2, double y2, double halfWidth, HashSet<(int, int)> cells)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001) return;

        // Normalize direction
        dx /= length;
        dy /= length;

        // Sample points along the line
        double step = CellSizeMicrometers * 0.5;
        for (double t = 0; t <= length; t += step)
        {
            double px = x1 + dx * t;
            double py = y1 + dy * t;
            MarkCircleAsCells(px, py, halfWidth, cells);
        }
    }

    /// <summary>
    /// Marks cells in a circle around a point.
    /// </summary>
    private void MarkCircleAsCells(double cx, double cy, double radius, HashSet<(int, int)> cells)
    {
        var (gcx, gcy) = PhysicalToGrid(cx, cy);
        int gridRadius = (int)Math.Ceiling(radius / CellSizeMicrometers);

        for (int gx = gcx - gridRadius; gx <= gcx + gridRadius; gx++)
        {
            for (int gy = gcy - gridRadius; gy <= gcy + gridRadius; gy++)
            {
                if (IsInBounds(gx, gy))
                {
                    // Check if cell center is within radius
                    var (px, py) = GridToPhysical(gx, gy);
                    double dist = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    if (dist <= radius)
                    {
                        cells.Add((gx, gy));
                    }
                }
            }
        }
    }
}
