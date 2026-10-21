namespace CAP_Core.Grid.GroupCollision;

/// <summary>
/// Uniform-grid spatial hash over obstacle primitives so each moved primitive is only
/// tested against the obstacles near it — keeps the drag-time check linear in the size
/// of the dragged group instead of quadratic. Primitives spanning very many cells (die
/// sized footprints) are kept in a short "oversized" list that every query scans.
/// </summary>
internal sealed class CollisionSpatialIndex
{
    /// <summary>Cell edge length in µm — a few typical component/bend sizes.</summary>
    private const double CellSizeMicrometers = 50.0;

    /// <summary>Above this many cells a primitive goes to the oversized list instead.</summary>
    private const int MaxCellsPerPrimitive = 256;

    private readonly Dictionary<(int X, int Y), List<CollisionPrimitive>> _cells = new();
    private readonly List<CollisionPrimitive> _oversized = new();
    private int _stamp;
    private readonly double _cellSize;

    /// <summary>Builds the index over <paramref name="obstacles"/>.</summary>
    public CollisionSpatialIndex(IReadOnlyList<CollisionPrimitive> obstacles, double cellSize = CellSizeMicrometers)
    {
        _cellSize = cellSize;
        foreach (var obstacle in obstacles)
            Insert(obstacle);
    }

    /// <summary>
    /// Calls <paramref name="visit"/> once for every obstacle whose box comes within
    /// <paramref name="reach"/> of the given box, stopping early when it returns true.
    /// </summary>
    /// <returns>True when a visit returned true.</returns>
    public bool AnyNear(
        double minX, double minY, double maxX, double maxY, double reach, Func<CollisionPrimitive, bool> visit)
    {
        _stamp++;
        foreach (var obstacle in _oversized)
        {
            if (Near(obstacle, minX, minY, maxX, maxY, reach) && visit(obstacle))
                return true;
        }

        var (x0, y0, x1, y1) = CellRange(minX - reach, minY - reach, maxX + reach, maxY + reach);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (!_cells.TryGetValue((x, y), out var bucket))
                    continue;
                foreach (var obstacle in bucket)
                {
                    if (obstacle.QueryStamp == _stamp)
                        continue;
                    obstacle.QueryStamp = _stamp;
                    if (Near(obstacle, minX, minY, maxX, maxY, reach) && visit(obstacle))
                        return true;
                }
            }
        }
        return false;
    }

    private void Insert(CollisionPrimitive obstacle)
    {
        var (x0, y0, x1, y1) = CellRange(obstacle.MinX, obstacle.MinY, obstacle.MaxX, obstacle.MaxY);
        long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
        if (cells > MaxCellsPerPrimitive)
        {
            _oversized.Add(obstacle);
            return;
        }
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (!_cells.TryGetValue((x, y), out var bucket))
                {
                    bucket = new List<CollisionPrimitive>();
                    _cells[(x, y)] = bucket;
                }
                bucket.Add(obstacle);
            }
        }
    }

    private static bool Near(CollisionPrimitive o, double minX, double minY, double maxX, double maxY, double reach) =>
        o.MinX - o.HalfWidth <= maxX + reach && o.MaxX + o.HalfWidth >= minX - reach
        && o.MinY - o.HalfWidth <= maxY + reach && o.MaxY + o.HalfWidth >= minY - reach;

    private (int X0, int Y0, int X1, int Y1) CellRange(double minX, double minY, double maxX, double maxY) =>
        (Cell(minX), Cell(minY), Cell(maxX), Cell(maxY));

    private int Cell(double coordinate) =>
        (int)Math.Floor(Math.Clamp(coordinate / _cellSize, int.MinValue / 2.0, int.MaxValue / 2.0));
}
