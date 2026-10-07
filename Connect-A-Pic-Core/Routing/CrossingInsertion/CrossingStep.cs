using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// One planned crossing of a crossing-aware route: where the search jumped across another
/// waveguide, and which waveguide that was.
/// </summary>
/// <param name="CrossedConnection">The routed connection the new route crosses.</param>
/// <param name="CenterX">Crossing centre X (µm) — on the crossed waveguide's axis.</param>
/// <param name="CenterY">Crossing centre Y (µm).</param>
public readonly record struct PlannedCrossing(Guid CrossedConnection, double CenterX, double CenterY);

/// <summary>
/// The crossing move of a crossing-aware A* search: where the next cell is blocked by
/// another waveguide, the search may jump straight across it — as a placed crossing
/// component will later carry both signals — but only where a crossing physically fits:
/// the move is axis-aligned and arrives straight, the blocking waveguide is exactly one
/// straight segment perpendicular to the move with enough straight run on both sides,
/// and the crossing's footprint and landing cells hold nothing else.
/// </summary>
public sealed class CrossingStep
{
    private readonly PathfindingGrid _grid;
    private readonly double _halfFootprint;

    /// <summary>Creates the step for crossings of <paramref name="crossingEdgeMicrometers"/> edge length.</summary>
    /// <param name="grid">The routing grid with the registered waveguides.</param>
    /// <param name="crossingEdgeMicrometers">Edge length of the crossing component (µm).</param>
    /// <param name="clearanceMicrometers">Straight run kept beyond the crossing ports on both wires (µm).</param>
    /// <param name="penaltyCost">Extra search cost of one crossing, in µm of equivalent path length.</param>
    public CrossingStep(PathfindingGrid grid, double crossingEdgeMicrometers, double clearanceMicrometers, double penaltyCost)
    {
        _grid = grid;
        _halfFootprint = crossingEdgeMicrometers / 2 + clearanceMicrometers;
        PenaltyCost = penaltyCost;
        ApproachCells = (int)Math.Ceiling(_halfFootprint / grid.CellSizeMicrometers);
    }

    /// <summary>Extra search cost of one crossing (µm-equivalent).</summary>
    public double PenaltyCost { get; }

    /// <summary>Straight run (cells) the search must have before it may jump.</summary>
    public int ApproachCells { get; }

    /// <summary>
    /// Tries to jump from cell (<paramref name="x"/>, <paramref name="y"/>) in cardinal
    /// direction (<paramref name="dx"/>, <paramref name="dy"/>) across the waveguide blocking
    /// the next cell. Returns false when no crossing fits there.
    /// </summary>
    /// <param name="x">Current cell X.</param>
    /// <param name="y">Current cell Y.</param>
    /// <param name="dx">Step X (−1, 0 or 1).</param>
    /// <param name="dy">Step Y (−1, 0 or 1; exactly one of dx, dy is non-zero).</param>
    /// <param name="straightRun">Straight cells the search has run in this direction.</param>
    /// <param name="spanCells">Cells the jump advances.</param>
    /// <param name="crossing">The planned crossing.</param>
    public bool TryJump(int x, int y, int dx, int dy, int straightRun, out int spanCells, out PlannedCrossing crossing)
    {
        spanCells = 0;
        crossing = default;
        if (dx != 0 == (dy != 0) || straightRun < ApproachCells) return false;
        if (_grid.GetCellState(x + dx, y + dy) != BlockedByWaveguide) return false;

        var (nextX, nextY) = _grid.GridToPhysical(x + dx, y + dy);
        var blockers = _grid.StraightSegmentsAt(nextX, nextY);
        if (blockers.Count != 1) return false;
        var segment = blockers[0];
        bool movingHorizontally = dx != 0;
        if (segment.IsHorizontal == movingHorizontally) return false;

        var (currentX, currentY) = _grid.GridToPhysical(x, y);
        double centerX = movingHorizontally ? segment.CrossCoordinate : currentX;
        double centerY = movingHorizontally ? currentY : segment.CrossCoordinate;
        if (!HasStraightRunAround(segment, movingHorizontally ? centerY : centerX)) return false;

        double toCenter = movingHorizontally ? Math.Abs(centerX - currentX) : Math.Abs(centerY - currentY);
        spanCells = (int)Math.Ceiling((toCenter + _halfFootprint) / _grid.CellSizeMicrometers);
        if (!IsPassable(x, y, dx, dy, spanCells, segment.Owner) || !IsFootprintClear(centerX, centerY, segment.Owner))
            return false;

        crossing = new PlannedCrossing(segment.Owner, centerX, centerY);
        return true;
    }

    private const byte Free = 0;
    private const byte BlockedByWaveguide = 2;

    private bool HasStraightRunAround(PathfindingGrid.StraightWaveguideSegment segment, double along)
    {
        var (min, max) = segment.AxisRange;
        return along - min >= _halfFootprint && max - along >= _halfFootprint;
    }

    /// <summary>Every jumped cell is free or the crossed waveguide's own; the landing cell is free.</summary>
    private bool IsPassable(int x, int y, int dx, int dy, int spanCells, Guid owner)
    {
        for (int k = 1; k <= spanCells; k++)
        {
            int cx = x + dx * k, cy = y + dy * k;
            byte state = _grid.GetCellState(cx, cy);
            if (state == Free) continue;
            if (k == spanCells || state != BlockedByWaveguide || !_grid.IsCellOfWaveguide(owner, cx, cy))
                return false;
        }
        return true;
    }

    /// <summary>The crossing body (plus clearance) holds no component and no third waveguide.</summary>
    private bool IsFootprintClear(double centerX, double centerY, Guid owner)
    {
        var (gx1, gy1) = _grid.PhysicalToGrid(centerX - _halfFootprint, centerY - _halfFootprint);
        var (gx2, gy2) = _grid.PhysicalToGrid(centerX + _halfFootprint, centerY + _halfFootprint);
        for (int gx = gx1; gx <= gx2; gx++)
        for (int gy = gy1; gy <= gy2; gy++)
        {
            byte state = _grid.GetCellState(gx, gy);
            if (state == Free) continue;
            if (state != BlockedByWaveguide || !_grid.IsCellOfWaveguide(owner, gx, gy)) return false;
        }
        return true;
    }
}
