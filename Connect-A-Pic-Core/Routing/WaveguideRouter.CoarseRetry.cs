using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// The coarse-grid retry of <see cref="WaveguideRouter"/> (issue #1418): before a wire is
/// degraded to a blocked fallback, the A* runs once more on a coarser copy of the obstacle
/// grid. On the fine grid the search can flood the huge free plane between component rows
/// and exhaust its node budget before reaching an empty detour lane (the RAM word-cell
/// select trunk, issue #1412); the coarse grid has factor² fewer cells, so the same budget
/// reaches the lane. A coarse result is only accepted after smoothing AND a collision check
/// on the fine grid, so no new overlaps sneak in.
/// </summary>
public partial class WaveguideRouter
{
    /// <summary>
    /// Cell-size multiplier for the coarse-grid retry (issue #1418). 4 means the retry grid
    /// has 4× larger cells (16× fewer cells). 1 or less disables the retry.
    /// </summary>
    public int CoarseRetryCellSizeFactor { get; set; } = 4;

    /// <summary>
    /// Total node expansions the coarse-grid retries inside the most recent
    /// <see cref="Route"/> call burned — summed over both the factor-4 and the
    /// factor-8 escalation attempts and their lateral-tolerance retries. Diagnostic
    /// surface (issue #1426): lets the perf-guard test measure what the retry
    /// actually costs per blocked wire.
    /// </summary>
    public long LastRouteCoarseNodesExpanded { get; private set; }

    /// <summary>
    /// Coarse-retry budget as a multiple of <see cref="Phase2MaxNodes"/> (issue #1426).
    /// A single <see cref="Route"/> call may burn at most this many × Phase2MaxNodes
    /// node expansions across all its coarse-retry attempts (factor-4 strict+tolerant
    /// plus the factor-8 escalation strict+tolerant, possibly repeated at the
    /// connection radius when the process floor fails). Bounds the worst-case cost
    /// one blocked wire adds to a re-route: the measurement on the word-cell
    /// contention scene showed a happy-path retry needs ~9× (269k expansions at
    /// 30k phase-2 budget), so 16× leaves comfortable headroom while capping a
    /// flooded sealed-pin retry 20× below the uncapped worst case of
    /// (16+16+64+64)×2 radii = 320×.
    /// </summary>
    public int CoarseRetryBudgetMultiplier { get; set; } = 16;

    /// <summary>
    /// Effective cap on the total node expansions a single <see cref="Route"/> call may
    /// burn across all its coarse-retry attempts — <see cref="CoarseRetryBudgetMultiplier"/>
    /// × <see cref="Phase2MaxNodes"/>.
    /// </summary>
    private int MaxCoarseRetryNodesPerWire =>
        (int)Math.Min(int.MaxValue, (long)CoarseRetryBudgetMultiplier * Phase2MaxNodes);

    /// <summary>
    /// Fires when the router is about to enter the coarse-grid retry for a wire.
    /// Diagnostic hook (issue #1426): lets tests cancel during the retry to verify
    /// the bounded-exit guarantee.
    /// </summary>
    internal Action? OnCoarseRetryStarted { get; set; }

    /// <summary>
    /// Fires at the end of every <see cref="Route"/> call with the total coarse-retry
    /// node expansions that wire burned (0 when the retry never ran). Diagnostic hook
    /// (issue #1426): lets tests log per-wire retry cost across a full manager pass.
    /// </summary>
    internal Action<long>? OnRouteCoarseExpansionsRecorded { get; set; }

    /// <summary>
    /// Runs a single A* attempt on a coarse copy of <see cref="PathfindingGrid"/> with the
    /// full <see cref="Phase2MaxNodes"/> budget, escalating to twice
    /// <see cref="CoarseRetryCellSizeFactor"/> when the first coarse grid still floods the
    /// budget (at 4× a huge free plane can still swallow 30k expansions before the search
    /// reaches a distant detour lane; at 8× the flood saturates). Returns the smoothed path
    /// when it connects, does not intersect itself, and is collision-free on the FINE grid
    /// (validated with the same pin-corridor allowances the direct-candidate check uses);
    /// otherwise null.
    /// </summary>
    internal RoutedPath? TryRouteCoarseAStar(double bendRadius,
                                             double startX, double startY, double startAngle,
                                             double endX, double endY, double endInputAngle,
                                             PhysicalPin startPin, PhysicalPin endPin,
                                             CancellationToken cancellationToken = default)
    {
        if (PathfindingGrid == null || CoarseRetryCellSizeFactor <= 1) return null;
        if (cancellationToken.IsCancellationRequested) return null;

        OnCoarseRetryStarted?.Invoke();

        var (path, provedNoPath) = TryRouteCoarseAStarAtFactor(
            CoarseRetryCellSizeFactor, bendRadius,
            startX, startY, startAngle, endX, endY, endInputAngle,
            startPin, endPin, cancellationToken);
        if (path != null || provedNoPath)
        {
            // A proven no-path verdict at the first factor holds at the coarser one too:
            // its free cells are a subset of this grid's, so escalating would only
            // re-flood the same plane for nothing.
            return path;
        }
        // Skip the escalation once the per-wire budget is spent — a factor-8 flood on
        // top of an exhausted factor-4 search only multiplies the blocked wire's cost.
        if (LastRouteCoarseNodesExpanded >= MaxCoarseRetryNodesPerWire) return null;
        return TryRouteCoarseAStarAtFactor(CoarseRetryCellSizeFactor * 2, bendRadius,
            startX, startY, startAngle, endX, endY, endInputAngle,
            startPin, endPin, cancellationToken).Path;
    }

    /// <summary>
    /// The coarse attempt's node budget: <see cref="Phase2MaxNodes"/> times factor²,
    /// clamped so the running total across the wire's retries stays under
    /// <see cref="MaxCoarseRetryNodesPerWire"/> (issue #1426).
    /// </summary>
    private int CoarseBudget(int factor)
    {
        long factorSquared = (long)Phase2MaxNodes * factor * factor;
        long remaining = MaxCoarseRetryNodesPerWire - LastRouteCoarseNodesExpanded;
        return (int)Math.Min(int.MaxValue, Math.Min(factorSquared, Math.Max(0, remaining)));
    }

    /// <summary>
    /// One coarse-grid A* attempt at a specific cell-size factor. <c>ProvedNoPath</c> is
    /// true when the search itself proved no route exists at this resolution (open set
    /// emptied with the goal vicinity never reached) — retries and escalations are then
    /// futile and skipped.
    /// </summary>
    private (RoutedPath? Path, bool ProvedNoPath) TryRouteCoarseAStarAtFactor(
        int factor, double bendRadius,
        double startX, double startY, double startAngle,
        double endX, double endY, double endInputAngle,
        PhysicalPin startPin, PhysicalPin endPin,
        CancellationToken cancellationToken)
    {
        var coarseGrid = PathfindingGrid!.CreateCoarseCopy(factor);

        double corridorLength = bendRadius * 3;
        double corridorWidth = bendRadius;
        double endFacingAngle = AngleUtilities.NormalizeAngle(endInputAngle + 180);

        // Same corridor allowances as the fine attempt: the pins sit inside their own
        // components' blocked cells, and dense siblings bury the pins' outward axes.
        coarseGrid.ClearPinCorridor(startX, startY, startAngle, corridorLength, corridorWidth);
        coarseGrid.ClearPinCorridor(endX, endY, endFacingAngle, corridorLength, corridorWidth);
        coarseGrid.ClearPinCorridor(endX, endY, endInputAngle, corridorLength, corridorWidth);
        coarseGrid.ClearPinFanoutWaveguideCells(startX, startY, startAngle, corridorLength);
        coarseGrid.ClearPinFanoutWaveguideCells(endX, endY, endFacingAngle, corridorLength);

        var costCalculator = CreateCoarseCostCalculator(
            coarseGrid, bendRadius, startX, startY, endX, endY);

        var (gridStartX, gridStartY) = coarseGrid.PhysicalToGrid(startX, startY);
        var (gridEndX, gridEndY) = coarseGrid.PhysicalToGrid(endX, endY);
        var startDir = GridDirectionExtensions.FromAngle(startAngle);
        var endDir = GridDirectionExtensions.FromAngle(endInputAngle);

        // Budget: the fine Phase-2 budget scaled by factor² — the coarse flood covers
        // factor² fewer cells per expansion, so this keeps the same area coverage the
        // fine search had when it exhausted itself. Saturation bounds the cost: the
        // search ends when the coarse open set empties, long before the budget on
        // small grids.
        int coarseBudget = CoarseBudget(factor);

        var astar = new AStarPathfinder.AStarPathfinder(coarseGrid, costCalculator)
        {
            MaxNodesExpanded = coarseBudget,
            UseDiagonals = UseDiagonalRouting
        };
        var gridPath = astar.FindPath(gridStartX, gridStartY, startDir,
                                      gridEndX, gridEndY, endDir, cancellationToken);
        LastRouteCoarseNodesExpanded += astar.LastSearchNodesExpanded;

        // Same lateral-tolerance retry as the fine attempt: coarse cells quantize the
        // pin's entry axis, so an exact on-axis arrival can be impossible even when a
        // reachably-close cell exists; the smoother snaps the final approach onto the axis.
        // Skipped when the strict run proved no tolerant-goal state is reachable — the
        // retry would re-flood the same exhausted open set and fail identically.
        bool provedNoPath = astar.LastSearchProvedNoPath && !astar.LastSearchReachedGoalVicinity;
        if (gridPath == null && !provedNoPath && !cancellationToken.IsCancellationRequested)
        {
            // Re-derive the budget AFTER the strict run: the per-wire cap
            // (issue #1426) is shared between the two, so the retry only gets
            // what the strict attempt left unspent.
            int tolerantBudget = CoarseBudget(factor);
            var tolerantRetry = new AStarPathfinder.AStarPathfinder(coarseGrid, costCalculator)
            {
                MaxNodesExpanded = tolerantBudget,
                AllowLateralGoalTolerance = true,
                UseDiagonals = UseDiagonalRouting
            };
            gridPath = tolerantRetry.FindPath(gridStartX, gridStartY, startDir,
                                              gridEndX, gridEndY, endDir, cancellationToken);
            LastRouteCoarseNodesExpanded += tolerantRetry.LastSearchNodesExpanded;
            provedNoPath = tolerantRetry.LastSearchProvedNoPath;
        }
        if (gridPath == null || gridPath.Count < 2)
        {
            return (null, provedNoPath);
        }

        var smoother = new PathSmoother(coarseGrid, bendRadius, AllowedRadiiIncluding(bendRadius));
        var smoothedPath = smoother.ConvertToSegments(gridPath, startPin, endPin);
        if (smoothedPath.Segments.Count == 0 || smoothedPath.IsInvalidGeometry) return (null, false);

        // The coarse grid's blocked set is a superset of the fine grid's, but the smoothed
        // geometry is new — arcs and corridor exits are re-checked on the fine grid with
        // the same component-cell and sibling-geometry verdicts the direct candidate gets.
        if (!IsCleanAStarResult(smoothedPath)) return (null, false);
        if (IsDirectCandidateBlockedByComponents(smoothedPath.Segments, startPin, endPin, bendRadius)
            || DirectCandidateConflictsWithSibling(smoothedPath, startPin, endPin, bendRadius))
        {
            return (null, false);
        }

        smoothedPath.DebugGridPath = gridPath;
        return (smoothedPath, false);
    }

    /// <summary>
    /// Cost model for the coarse attempt: same radius-synchronized straight-run and
    /// pin-escape scaling as the fine attempt, but expressed in coarse cells and kept
    /// independent of the router's shared <see cref="CostCalculator"/> (which stays synced
    /// to the fine grid).
    /// </summary>
    private RoutingCostCalculator CreateCoarseCostCalculator(
        PathfindingGrid coarseGrid, double bendRadius,
        double startX, double startY, double endX, double endY)
    {
        double cellSize = coarseGrid.CellSizeMicrometers;
        var (gridStartX, gridStartY) = coarseGrid.PhysicalToGrid(startX, startY);
        var (gridEndX, gridEndY) = coarseGrid.PhysicalToGrid(endX, endY);
        int gridDistance = Math.Abs(gridEndX - gridStartX) + Math.Abs(gridEndY - gridStartY);

        int pinTangentEscape = Math.Max(2, (int)(bendRadius / cellSize) + 1);
        int straightRun = (int)Math.Ceiling(bendRadius * 2 / cellSize);

        return new RoutingCostCalculator
        {
            CellSizeMicrometers = cellSize,
            MinBendRadiusMicrometers = bendRadius,
            MinPinEscapeCells = Math.Min(pinTangentEscape, Math.Max(2, gridDistance / 6)),
            MinStraightRunCells = Math.Min(straightRun, Math.Max(2, gridDistance / 4)),
            UseDiagonals = UseDiagonalRouting,
        };
    }
}
