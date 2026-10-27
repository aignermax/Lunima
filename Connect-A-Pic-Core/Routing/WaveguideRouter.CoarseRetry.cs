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
    private readonly CoarseRouteRetry _coarseRetry = new();
    private CoarseRouteAcceptance? _acceptCoarseRoute;

    /// <summary>
    /// Cell-size multiplier for the coarse-grid retry (issue #1418). 4 means the retry grid
    /// has 4× larger cells (16× fewer cells). 1 or less disables the retry.
    /// </summary>
    public int CoarseRetryCellSizeFactor { get; set; } = 4;

    /// <summary>
    /// Total node expansions the coarse-grid retries inside the most recent
    /// <see cref="Route"/> call burned — summed over both the factor-4 and the
    /// factor-8 escalation attempts and their lateral-tolerance retries. Diagnostic
    /// surface: lets the perf-guard test measure what the retry
    /// actually costs per blocked wire.
    /// </summary>
    public long LastRouteCoarseNodesExpanded => _coarseRetry.NodesExpanded;

    /// <summary>
    /// Coarse-retry budget as a multiple of <see cref="Phase2MaxNodes"/>.
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
    /// Fires when the router is about to enter the coarse-grid retry for a wire.
    /// Diagnostic hook: lets tests cancel during the retry to verify
    /// the bounded-exit guarantee.
    /// </summary>
    internal Action? OnCoarseRetryStarted { get; set; }

    /// <summary>
    /// Fires at the end of every <see cref="Route"/> call with the total coarse-retry
    /// node expansions that wire burned (0 when the retry never ran). Diagnostic hook:
    /// lets tests log per-wire retry cost across a full manager pass.
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

        // Read settings after the hook: observers may change the retry budget or cancel.
        var settings = new CoarseRouteRetry.Settings(CoarseRetryCellSizeFactor, Phase2MaxNodes,
            CoarseRetryBudgetMultiplier, UseDiagonalRouting);
        return _coarseRetry.TryRoute(PathfindingGrid!, settings, _acceptCoarseRoute ??= AcceptCoarseRoute,
            bendRadius, startX, startY, startAngle, endX, endY, endInputAngle,
            startPin, endPin, cancellationToken);
    }

    private RoutedPath? AcceptCoarseRoute(PathfindingGrid coarseGrid, List<AStarNode> gridPath,
        double bendRadius, PhysicalPin startPin, PhysicalPin endPin)
    {
        var smoother = new PathSmoother(coarseGrid, bendRadius, AllowedRadiiIncluding(bendRadius));
        var smoothedPath = smoother.ConvertToSegments(gridPath, startPin, endPin);
        if (smoothedPath.Segments.Count == 0 || smoothedPath.IsInvalidGeometry) return null;

        // The coarse grid's blocked set is a superset of the fine grid's, but the smoothed
        // geometry is new — arcs and corridor exits are re-checked on the fine grid with
        // the same component-cell and sibling-geometry verdicts the direct candidate gets.
        if (!IsCleanAStarResult(smoothedPath)) return null;
        if (IsDirectCandidateBlockedByComponents(smoothedPath.Segments, startPin, endPin, bendRadius)
            || RunsThroughBreakoutReservation(smoothedPath.Segments)
            || DirectCandidateConflictsWithSibling(smoothedPath, startPin, endPin, bendRadius))
        {
            return null;
        }

        smoothedPath.DebugGridPath = gridPath;
        return smoothedPath;
    }

}
