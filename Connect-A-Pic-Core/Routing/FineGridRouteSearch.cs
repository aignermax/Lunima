using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Routing;

/// <summary>
/// Fine-grid reachability and search retry policy. Settings remain mutable between retries so
/// escalation observers can change subsequent searches without rebuilding the routing owner.
/// The caller owns corridor restoration, cost restoration and geometric acceptance.
/// </summary>
internal sealed class FineGridRouteSearch
{
    internal bool UseDiagonals { get; set; } = true;
    internal int Phase1MaxNodes { get; set; } = 200_000;
    internal int Phase2MaxNodes { get; set; } = 2_000_000;
    internal Action? OnComplexRouteStarted { get; set; }

    internal List<AStarNode>? FindPath(PathfindingGrid grid, RoutingCostCalculator costCalculator,
        HierarchicalPathfinder? hierarchicalPathfinder, CrossingStep? crossingStep, int? crossingMaxNodes,
        int gridStartX, int gridStartY, GridDirection startDir,
        int gridEndX, int gridEndY, GridDirection endDir, int scaledEscape,
        CancellationToken cancellationToken, out bool lastGoalReachable)
    {
        int gridDistance = Math.Abs(gridEndX - gridStartX) + Math.Abs(gridEndY - gridStartY);
        List<AStarNode>? gridPath = null;
        // Reachability gate: a cheap flood fill over the whole grid decides whether
        // ANY path to the goal region can exist at all. The flood ignores direction,
        // turn and pin-escape constraints, so its reachable set is a superset of what
        // the constrained searches below could ever reach — a negative verdict lets
        // every search (windowed, full-grid, tolerant and minimal-constraint retries)
        // be skipped with the same null result they would have returned after burning
        // their full node budgets. The flood sees the same grid state the searches
        // would: the pin corridors above are already cleared.
        // The flood treats every waveguide as a wall, so it cannot judge a search that
        // may cross them — with crossings allowed, the search itself decides.
        bool goalReachable = hierarchicalPathfinder != null
            || crossingStep != null
            || grid.CanReachGoalDirected(
                gridStartX, gridStartY, gridEndX, gridEndY,
                AStarPathfinder.AStarPathfinder.DefaultGoalTolerance, UseDiagonals);
        lastGoalReachable = goalReachable;

        if (hierarchicalPathfinder != null)
        {
            gridPath = hierarchicalPathfinder.FindPath(
                gridStartX, gridStartY, startDir,
                gridEndX, gridEndY, endDir);
        }
        else if (goalReachable)
        {
            gridPath = RunAStarPhases(grid, costCalculator, crossingMaxNodes, gridStartX, gridStartY, startDir,
                                      gridEndX, gridEndY, endDir, crossingStep, cancellationToken);
        }

        // Lateral-tolerance retry: the strict phases require an exact
        // on-axis arrival, which is impossible when another waveguide
        // crosses the pin's entry axis outside the cleared corridor.
        // Retry accepting a small lateral offset; the smoother snaps the
        // final approach onto the axis. Only otherwise-blocked routes
        // reach this point, so successful routes are unaffected.
        if (gridPath == null && goalReachable && !cancellationToken.IsCancellationRequested)
        {
            var tolerantRetry = new AStarPathfinder.AStarPathfinder(grid, costCalculator)
            {
                MaxNodesExpanded = Phase1MaxNodes,
                AllowLateralGoalTolerance = true,
                UseDiagonals = UseDiagonals,
                Crossings = crossingStep
            };
            gridPath = tolerantRetry.FindPath(gridStartX, gridStartY, startDir,
                                              gridEndX, gridEndY, endDir, cancellationToken);
        }

        // Loop detection: if path is >2× Manhattan distance, retry with minimal constraints
        if (gridPath != null && gridPath.Count > gridDistance * 2 && scaledEscape > 2)
        {
            costCalculator.MinPinEscapeCells = 2;
            costCalculator.MinStraightRunCells = 2;
            var retry = new AStarPathfinder.AStarPathfinder(grid, costCalculator)
            {
                UseDiagonals = UseDiagonals,
                Crossings = crossingStep
            };
            var retryPath = retry.FindPath(gridStartX, gridStartY, startDir,
                                           gridEndX, gridEndY, endDir, cancellationToken);
            if (retryPath != null && retryPath.Count < gridPath.Count)
                gridPath = retryPath;
        }

        if ((gridPath == null || gridPath.Count < 2) && goalReachable)
        {
            costCalculator.MinPinEscapeCells = 2;
            costCalculator.MinStraightRunCells = 2;
            var fallback = new AStarPathfinder.AStarPathfinder(grid, costCalculator)
            {
                UseDiagonals = UseDiagonals,
                Crossings = crossingStep
            };
            gridPath = fallback.FindPath(gridStartX, gridStartY, startDir,
                                         gridEndX, gridEndY, endDir, cancellationToken);
        }

        return gridPath;
    }

    private List<AStarNode>? RunAStarPhases(
        PathfindingGrid grid, RoutingCostCalculator costCalculator, int? crossingMaxNodes,
        int gridStartX, int gridStartY, GridDirection startDir,
        int gridEndX, int gridEndY, GridDirection endDir,
        CrossingStep? crossingStep,
        CancellationToken cancellationToken)
    {
        var search = new AStarPathfinder.AStarPathfinder(grid, costCalculator)
        {
            // The continuous search replays the exact expansion sequence of the
            // former quick-then-extended phases (the extended phase re-expanded the
            // quick phase's prefix identically), so the extended phase's budget IS
            // the total budget — the quick phase survives only as the escalation mark.
            MaxNodesExpanded = crossingMaxNodes ?? Phase2MaxNodes,
            UseDiagonals = UseDiagonals,
            EscalationThresholdNodes = Phase1MaxNodes,
            OnEscalationThresholdReached = () => OnComplexRouteStarted?.Invoke(),
            Crossings = crossingStep
        };
        return search.FindPath(gridStartX, gridStartY, startDir,
                               gridEndX, gridEndY, endDir, cancellationToken);
    }

}
