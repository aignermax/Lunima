using CAP_Core.Routing.Grid;
using CAP_Core.Components;

namespace CAP_Core.Routing.Grid;

/// <summary>
/// Manages obstacle tracking on a PathfindingGrid.
/// Handles component obstacles, waveguide obstacles, and pin reservation zones.
/// </summary>
public class GridObstacleManager
{
    private readonly PathfindingGrid _grid;

    private readonly Dictionary<Component, HashSet<(int x, int y)>> _componentCells = new();
    private readonly Dictionary<Guid, HashSet<(int x, int y)>> _waveguideCells = new();
    private readonly HashSet<(int x, int y)> _pinZoneCells = new();

    /// <summary>
    /// Called after waveguide cells are added, passing the newly marked cells.
    /// </summary>
    public Action<IEnumerable<(int x, int y)>>? OnWaveguideCellsAdded { get; set; }

    /// <summary>
    /// Called after all waveguide obstacles are cleared.
    /// </summary>
    public Action? OnAllWaveguidesCleared { get; set; }

    public GridObstacleManager(PathfindingGrid grid)
    {
        _grid = grid;
    }

    public void Clear()
    {
        _componentCells.Clear();
        _waveguideCells.Clear();
        _pinZoneCells.Clear();
    }

    public bool IsPinReservationZone(int gridX, int gridY) => _pinZoneCells.Contains((gridX, gridY));

    /// <summary>
    /// Marks cells blocked by a component's bounding box (with padding).
    /// Leaves corridors open at pin positions so waveguides can connect.
    /// </summary>
    public void AddComponentObstacle(Component component)
    {
        double padding = _grid.ObstaclePaddingMicrometers;
        double x1 = component.PhysicalX - padding;
        double y1 = component.PhysicalY - padding;
        double x2 = component.PhysicalX + component.WidthMicrometers + padding;
        double y2 = component.PhysicalY + component.HeightMicrometers + padding;

        var (gx1, gy1) = _grid.PhysicalToGrid(x1, y1);
        var (gx2, gy2) = _grid.PhysicalToGrid(x2, y2);

        var pinCorridorCells = CollectPinCorridorCells(component);

        var cells = new HashSet<(int, int)>();
        for (int gx = gx1; gx <= gx2; gx++)
        {
            for (int gy = gy1; gy <= gy2; gy++)
            {
                if (_grid.IsInBounds(gx, gy) && !pinCorridorCells.Contains((gx, gy)))
                {
                    _grid.SetCellState(gx, gy, 1);
                    cells.Add((gx, gy));
                }
            }
        }
        _componentCells[component] = cells;

        double pinZoneRadius = 15.0;
        foreach (var pin in component.PhysicalPins)
        {
            var (pinX, pinY) = pin.GetAbsolutePosition();
            MarkPinReservationZone(pinX, pinY, pinZoneRadius);
        }
    }

    public void RemoveComponentObstacle(Component component)
    {
        if (_componentCells.TryGetValue(component, out var cells))
        {
            foreach (var (gx, gy) in cells)
            {
                if (_grid.IsInBounds(gx, gy))
                    _grid.SetCellState(gx, gy, 0);
            }
            _componentCells.Remove(component);
        }
    }

    public void UpdateComponentObstacle(Component component)
    {
        RemoveComponentObstacle(component);
        AddComponentObstacle(component);
    }

    /// <summary>
    /// Marks cells along a waveguide path as blocked (state=2).
    /// </summary>
    public void AddWaveguideObstacle(Guid connectionId, IEnumerable<PathSegment> segments,
                                      double waveguideWidth)
    {
        RemoveWaveguideObstacle(connectionId);

        var segmentList = segments.ToList();
        if (segmentList.Count == 0) return;

        var cells = new HashSet<(int, int)>();
        double halfWidth = waveguideWidth / 2;

        foreach (var segment in segmentList)
        {
            if (segment is StraightSegment straight)
            {
                MarkLineAsCells(straight.StartPoint.X, straight.StartPoint.Y,
                    straight.EndPoint.X, straight.EndPoint.Y, halfWidth, cells);
            }
            else if (segment is BendSegment bend)
            {
                MarkArcAsCells(bend, halfWidth, cells);
            }
        }

        foreach (var (gx, gy) in cells)
        {
            if (_grid.IsInBounds(gx, gy) && _grid.GetCellState(gx, gy) == 0)
                _grid.SetCellState(gx, gy, 2);
        }

        _waveguideCells[connectionId] = cells;
        OnWaveguideCellsAdded?.Invoke(cells);
    }

    public void RemoveWaveguideObstacle(Guid connectionId)
    {
        if (_waveguideCells.TryGetValue(connectionId, out var cells))
        {
            foreach (var (gx, gy) in cells)
            {
                if (_grid.IsInBounds(gx, gy) && _grid.GetCellState(gx, gy) == 2)
                    _grid.SetCellState(gx, gy, 0);
            }
            _waveguideCells.Remove(connectionId);
        }
    }

    public void ClearAllWaveguideObstacles()
    {
        foreach (var connectionId in _waveguideCells.Keys.ToList())
            RemoveWaveguideObstacle(connectionId);
        OnAllWaveguidesCleared?.Invoke();
    }

    // --- Private helpers ---

    private HashSet<(int, int)> CollectPinCorridorCells(Component component)
    {
        var pinCorridorCells = new HashSet<(int, int)>();
        double corridorLength = 10.0;
        double corridorWidth = 4.0;

        foreach (var pin in component.PhysicalPins)
        {
            var (pinX, pinY) = pin.GetAbsolutePosition();
            double pinAngle = pin.GetAbsoluteAngle();
            double angleRad = pinAngle * Math.PI / 180.0;
            double dx = Math.Cos(angleRad);
            double dy = Math.Sin(angleRad);
            double perpX = -dy;
            double perpY = dx;

            for (double dist = 0; dist <= corridorLength; dist += _grid.CellSizeMicrometers)
            {
                double centerX = pinX + dx * dist;
                double centerY = pinY + dy * dist;

                for (double offset = -corridorWidth / 2; offset <= corridorWidth / 2;
                     offset += _grid.CellSizeMicrometers)
                {
                    double cellX = centerX + perpX * offset;
                    double cellY = centerY + perpY * offset;
                    var (gx, gy) = _grid.PhysicalToGrid(cellX, cellY);
                    pinCorridorCells.Add((gx, gy));
                }
            }
        }
        return pinCorridorCells;
    }

    private void MarkPinReservationZone(double pinX, double pinY, double radiusMicrometers)
    {
        var (gcx, gcy) = _grid.PhysicalToGrid(pinX, pinY);
        int gridRadius = (int)Math.Ceiling(radiusMicrometers / _grid.CellSizeMicrometers);

        for (int gx = gcx - gridRadius; gx <= gcx + gridRadius; gx++)
        {
            for (int gy = gcy - gridRadius; gy <= gcy + gridRadius; gy++)
            {
                if (!_grid.IsInBounds(gx, gy)) continue;
                var (px, py) = _grid.GridToPhysical(gx, gy);
                double dist = Math.Sqrt((px - pinX) * (px - pinX) + (py - pinY) * (py - pinY));
                if (dist <= radiusMicrometers)
                    _pinZoneCells.Add((gx, gy));
            }
        }
    }

    private void MarkLineAsCells(double x1, double y1, double x2, double y2,
                                  double halfWidth, HashSet<(int, int)> cells)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001) return;

        dx /= length;
        dy /= length;

        double step = _grid.CellSizeMicrometers * 0.5;
        for (double t = 0; t <= length; t += step)
        {
            double px = x1 + dx * t;
            double py = y1 + dy * t;
            MarkCircleAsCells(px, py, halfWidth, cells);
        }
    }

    private void MarkArcAsCells(BendSegment bend, double halfWidth, HashSet<(int, int)> cells)
    {
        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        int numSamples = Math.Max(10, (int)(Math.Abs(bend.SweepAngleDegrees) / 5));

        for (int i = 0; i <= numSamples; i++)
        {
            double t = (double)i / numSamples;
            double angle = startRad + sweepRad * t;
            double sign = Math.Sign(bend.SweepAngleDegrees);
            if (sign == 0) sign = 1;

            double px = bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle - Math.PI / 2 * sign);
            double py = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle - Math.PI / 2 * sign);
            MarkCircleAsCells(px, py, halfWidth, cells);
        }
    }

    private void MarkCircleAsCells(double cx, double cy, double radius, HashSet<(int, int)> cells)
    {
        var (gcx, gcy) = _grid.PhysicalToGrid(cx, cy);
        int gridRadius = (int)Math.Ceiling(radius / _grid.CellSizeMicrometers);

        for (int gx = gcx - gridRadius; gx <= gcx + gridRadius; gx++)
        {
            for (int gy = gcy - gridRadius; gy <= gcy + gridRadius; gy++)
            {
                if (!_grid.IsInBounds(gx, gy)) continue;
                var (px, py) = _grid.GridToPhysical(gx, gy);
                double dist = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                if (dist <= radius)
                    cells.Add((gx, gy));
            }
        }
    }
}
