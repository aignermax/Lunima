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
/// other waveguides, the search may jump straight across them — one placed crossing
/// component per crossed wire, so a bundle of parallel wires is crossed by a row of
/// crossings — but only where the crossings physically fit: the move is axis-aligned and
/// arrives straight; every crossed wire is exactly one straight segment perpendicular to
/// the move with enough straight run on both sides; neighbouring crossings do not
/// overlap; nothing but the crossed wires lies in their footprints; and the route lands
/// on free cells behind the last one.
/// </summary>
public sealed class CrossingStep
{
    /// <summary>Longest jump (µm) — a bundle wider than this is not crossed in one move.</summary>
    private const double MaxJumpMicrometers = 120;

    /// <summary>Most crossings one jump may place.</summary>
    private const int MaxCrossingsPerJump = 8;

    private const byte Free = 0;
    private const byte BlockedByWaveguide = 2;

    private readonly PathfindingGrid _grid;
    private readonly double _edge;
    private readonly double _halfFootprint;
    private readonly double _clearRunAfter;
    private readonly int _maxScanCells;

    /// <summary>Creates the step for crossings of <paramref name="crossingEdgeMicrometers"/> edge length.</summary>
    /// <param name="grid">The routing grid with the registered waveguides.</param>
    /// <param name="crossingEdgeMicrometers">Edge length of the crossing component (µm).</param>
    /// <param name="clearanceMicrometers">Straight run kept beyond the crossing ports on both wires (µm).</param>
    /// <param name="penaltyCost">Extra search cost of one crossing, in µm of equivalent path length.</param>
    /// <param name="bendRadiusMicrometers">
    /// Bend radius of the route: the smoother rounds a turn with an arc that starts this far
    /// before the corner, so the approach must be that much longer to keep the crossing straight.
    /// </param>
    public CrossingStep(PathfindingGrid grid, double crossingEdgeMicrometers, double clearanceMicrometers,
                        double penaltyCost, double bendRadiusMicrometers = 0)
    {
        _grid = grid;
        _edge = crossingEdgeMicrometers;
        _halfFootprint = crossingEdgeMicrometers / 2 + clearanceMicrometers;
        _clearRunAfter = _halfFootprint + bendRadiusMicrometers;
        PenaltyCost = penaltyCost;
        ApproachCells = (int)Math.Ceiling((_halfFootprint + bendRadiusMicrometers) / grid.CellSizeMicrometers);
        _maxScanCells = (int)Math.Ceiling(MaxJumpMicrometers / grid.CellSizeMicrometers);
    }

    /// <summary>Extra search cost of one crossing (µm-equivalent).</summary>
    public double PenaltyCost { get; }

    /// <summary>Weight the search puts on its distance estimate while crossings are allowed (1 = optimal).</summary>
    public double HeuristicWeight { get; init; } = 1.0;

    /// <summary>
    /// Straight run (cells) the search must have before it may jump: the crossing's half
    /// footprint plus the bend radius, so the arc of the previous turn ends before it.
    /// </summary>
    public int ApproachCells { get; }

    /// <summary>
    /// Tries to jump from cell (<paramref name="x"/>, <paramref name="y"/>) in cardinal
    /// direction (<paramref name="dx"/>, <paramref name="dy"/>) across the waveguides blocking
    /// the cells ahead. Returns false when no row of crossings fits there.
    /// </summary>
    /// <param name="x">Current cell X.</param>
    /// <param name="y">Current cell Y.</param>
    /// <param name="dx">Step X (−1, 0 or 1).</param>
    /// <param name="dy">Step Y (−1, 0 or 1; exactly one of dx, dy is non-zero).</param>
    /// <param name="straightRun">Straight cells the search has run in this direction.</param>
    /// <param name="spanCells">Cells the jump advances.</param>
    /// <param name="runAfterCells">Straight cells between the last crossing and the landing cell.</param>
    /// <param name="crossings">The planned crossings, in travel order.</param>
    public bool TryJump(int x, int y, int dx, int dy, int straightRun,
                        out int spanCells, out int runAfterCells, out IReadOnlyList<PlannedCrossing> crossings)
    {
        spanCells = 0;
        runAfterCells = 0;
        crossings = Array.Empty<PlannedCrossing>();
        if (dx != 0 == (dy != 0) || straightRun < ApproachCells) return false;
        if (_grid.GetCellState(x + dx, y + dy) != BlockedByWaveguide) return false;

        var planned = new List<PlannedCrossing>();
        var (currentX, currentY) = _grid.GridToPhysical(x, y);
        bool horizontal = dx != 0;
        double origin = horizontal ? currentX : currentY;
        double direction = horizontal ? dx : dy;

        for (int k = 1; k <= _maxScanCells; k++)
        {
            int cx = x + dx * k, cy = y + dy * k;
            byte state = _grid.GetCellState(cx, cy);
            if (state == Free)
            {
                // Land only where the free straight behind the last crossing is long enough
                // for the crossing's half footprint plus the arc of a turn right after it —
                // a gap inside a wire bundle is not a landing, the next wire is crossed too.
                double runAfter = planned.Count == 0 ? 0
                    : Along(cx, cy, horizontal, origin, direction) - CenterAlong(planned[^1], horizontal, origin, direction);
                if (planned.Count > 0 && runAfter >= _clearRunAfter)
                {
                    if (!FootprintsClear(planned)) return false;
                    spanCells = k;
                    runAfterCells = (int)Math.Floor(runAfter / _grid.CellSizeMicrometers);
                    crossings = planned;
                    return true;
                }
                continue;
            }
            if (state != BlockedByWaveguide) return false;
            if (planned.Count > 0 && _grid.IsCellOfWaveguide(planned[^1].CrossedConnection, cx, cy)) continue;
            if (planned.Count == MaxCrossingsPerJump || !TryPlanCrossing(cx, cy, horizontal, currentX, currentY, planned))
                return false;
        }
        return false;
    }

    /// <summary>Plans the crossing of the wire blocking cell (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    private bool TryPlanCrossing(int cx, int cy, bool horizontal, double currentX, double currentY, List<PlannedCrossing> planned)
    {
        var (px, py) = _grid.GridToPhysical(cx, cy);
        var blockers = _grid.StraightSegmentsAt(px, py);
        if (blockers.Count != 1) return false;
        var segment = blockers[0];
        if (segment.IsHorizontal == horizontal) return false;

        double centerX = horizontal ? segment.CrossCoordinate : currentX;
        double centerY = horizontal ? currentY : segment.CrossCoordinate;
        var (min, max) = segment.AxisRange;
        double along = horizontal ? centerY : centerX;
        if (along - min < _halfFootprint || max - along < _halfFootprint) return false;
        if (planned.Count > 0)
        {
            var previous = planned[^1];
            double gap = horizontal ? Math.Abs(centerX - previous.CenterX) : Math.Abs(centerY - previous.CenterY);
            if (gap < _edge) return false; // the two crossing bodies would overlap
        }
        planned.Add(new PlannedCrossing(segment.Owner, centerX, centerY));
        return true;
    }

    private double Along(int cx, int cy, bool horizontal, double origin, double direction)
    {
        var (px, py) = _grid.GridToPhysical(cx, cy);
        return ((horizontal ? px : py) - origin) * direction;
    }

    private static double CenterAlong(PlannedCrossing crossing, bool horizontal, double origin, double direction) =>
        ((horizontal ? crossing.CenterX : crossing.CenterY) - origin) * direction;

    /// <summary>Each crossing body (plus clearance) holds no component and no wire but the ones this jump crosses.</summary>
    private bool FootprintsClear(List<PlannedCrossing> planned)
    {
        var crossed = planned.Select(p => p.CrossedConnection).ToList();
        foreach (var crossing in planned)
        {
            var (gx1, gy1) = _grid.PhysicalToGrid(crossing.CenterX - _halfFootprint, crossing.CenterY - _halfFootprint);
            var (gx2, gy2) = _grid.PhysicalToGrid(crossing.CenterX + _halfFootprint, crossing.CenterY + _halfFootprint);
            for (int gx = gx1; gx <= gx2; gx++)
            for (int gy = gy1; gy <= gy2; gy++)
            {
                byte state = _grid.GetCellState(gx, gy);
                if (state == Free) continue;
                if (state != BlockedByWaveguide || !crossed.Any(owner => _grid.IsCellOfWaveguide(owner, gx, gy)))
                    return false;
            }
        }
        return true;
    }
}
