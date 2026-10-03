using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// The A*-attempt half of <see cref="WaveguideRouter"/>: one obstacle-avoiding routing try at
/// a specific bend radius, with the cost model, pin corridors and path smoother synchronized
/// to that radius (split out to keep the router below the file-size limit).
/// </summary>
public partial class WaveguideRouter
{
    /// <summary>
    /// Attempts to route using A* pathfinding with obstacle avoidance at the given
    /// bend radius. The cost model (minimum straight run before turns) and the path smoother
    /// are synced to that radius, so the grid path leaves room for the arcs that will be built.
    /// Before any search runs, a direction-aware reachability flood
    /// (<see cref="PathfindingGrid.CanReachGoalDirected"/>) decides whether a path can exist
    /// at all — a proven-unreachable goal skips every search below with the same null result
    /// they would return after burning their full node budgets. The search itself runs first
    /// bounded to a corridor window around the endpoints (<see cref="ComputeSearchWindow"/>);
    /// when the window holds no path, the unbounded search runs with the extended node budget
    /// (<see cref="Phase2MaxNodes"/>), firing <see cref="OnComplexRouteStarted"/> at the
    /// quick-phase mark (<see cref="Phase1MaxNodes"/>).
    /// </summary>
    private bool TryRouteAStar(double bendRadius,
                                double startX, double startY, double startAngle,
                                double endX, double endY, double endInputAngle,
                                RoutedPath path, PhysicalPin startPin, PhysicalPin endPin,
                                CancellationToken cancellationToken = default)
    {
        if (PathfindingGrid == null) return false;

        LastAStarSearchWindow = null;

        // Sync the cost model to the radius of THIS attempt: turns must be spaced far
        // enough apart that the smoother can realize them as arcs of this radius.
        CostCalculator.MinBendRadiusMicrometers = bendRadius;
        CostCalculator.MinStraightRunCells =
            (int)Math.Ceiling(bendRadius * 2 / PathfindingGrid.CellSizeMicrometers);

        double corridorLength = bendRadius * 3;
        double corridorWidth = bendRadius;

        var clearedStart = PathfindingGrid.ClearPinCorridor(
            startX, startY, startAngle, corridorLength, corridorWidth);

        // Clear corridors in BOTH directions for the end pin:
        // 1. Facing direction (away from component) — ensures approach path is clear
        // 2. Input direction (into component) — ensures the terminal grid cell is reachable
        double endFacingAngle = AngleUtilities.NormalizeAngle(endInputAngle + 180);
        var clearedEndApproach = PathfindingGrid.ClearPinCorridor(
            endX, endY, endFacingAngle, corridorLength, corridorWidth);
        var clearedEndTerminal = PathfindingGrid.ClearPinCorridor(
            endX, endY, endInputAngle, corridorLength, corridorWidth);

        // Flat PDK components place pins closer together than one grid cell, so a sibling
        // route registered as an obstacle buries this pin. Clear only the single-cell line
        // of SIBLING cells along each pin's outward axis (foreign waveguides stay blocked).
        var clearedStartFanout = PathfindingGrid.ClearPinFanoutWaveguideCells(
            startX, startY, startAngle, corridorLength);
        var clearedEndFanout = PathfindingGrid.ClearPinFanoutWaveguideCells(
            endX, endY, endFacingAngle, corridorLength);

        try
        {
            var (gridStartX, gridStartY) = PathfindingGrid.PhysicalToGrid(startX, startY);
            var (gridEndX, gridEndY) = PathfindingGrid.PhysicalToGrid(endX, endY);

            var startDir = GridDirectionExtensions.FromAngle(startAngle);
            var endDir = GridDirectionExtensions.FromAngle(endInputAngle);

            int originalEscapeCells = CostCalculator.MinPinEscapeCells;

            // The forced straight run at a pin only needs to fit the first arc's tangent
            // length (r·tan(sweep/2), at most the bend radius for a 90° turn) — NOT the old
            // distance-scaled escape (up to 15 cells), which forced visibly different pin
            // clearances per connection. Close pins still degrade to 2 cells, as before.
            int gridDistance = Math.Abs(gridEndX - gridStartX) + Math.Abs(gridEndY - gridStartY);
            int scaledEscape = Math.Min(PinTangentEscapeCells(bendRadius),
                                        Math.Max(2, gridDistance / 6));
            CostCalculator.MinPinEscapeCells = scaledEscape;

            // Also scale MinStraightRunCells for close pins to allow tighter turns
            int originalStraightRun = CostCalculator.MinStraightRunCells;
            int scaledStraightRun = Math.Min(originalStraightRun, Math.Max(2, gridDistance / 4));
            CostCalculator.MinStraightRunCells = scaledStraightRun;

            List<AStarNode>? gridPath = null;

            // The heuristic's distance metric must match the movement model.
            CostCalculator.UseDiagonals = UseDiagonalRouting;

            // Reachability gate: a cheap flood fill over the whole grid decides whether
            // ANY path to the goal region can exist at all. The flood ignores direction,
            // turn and pin-escape constraints, so its reachable set is a superset of what
            // the constrained searches below could ever reach — a negative verdict lets
            // every search (windowed, full-grid, tolerant and minimal-constraint retries)
            // be skipped with the same null result they would have returned after burning
            // their full node budgets. The flood sees the same grid state the searches
            // would: the pin corridors above are already cleared.
            bool goalReachable = _hierarchicalPathfinder != null && UseHierarchicalPathfinding
                || PathfindingGrid.CanReachGoalDirected(
                    gridStartX, gridStartY, gridEndX, gridEndY,
                    AStarPathfinder.AStarPathfinder.DefaultGoalTolerance, UseDiagonalRouting);

            if (_hierarchicalPathfinder != null && UseHierarchicalPathfinding)
            {
                gridPath = _hierarchicalPathfinder.FindPath(
                    gridStartX, gridStartY, startDir,
                    gridEndX, gridEndY, endDir);
            }
            else
            {
                // Bounded attempt first: the search space is clipped to a corridor
                // window around the endpoints, so a wire on a large chip no longer
                // floods the whole grid before finding (or ruling out) a path.
                // When the window holds no path, the unbounded phases below run
                // exactly as before — the window can only change which valid path
                // is found, never whether one is found.
                var window = ComputeSearchWindow(gridStartX, gridStartY, gridEndX, gridEndY);
                LastAStarSearchWindow = window;
                if (goalReachable)
                {
                    gridPath = RunAStarPhases(window, gridStartX, gridStartY, startDir,
                                              gridEndX, gridEndY, endDir, cancellationToken);

                    if (gridPath == null && !cancellationToken.IsCancellationRequested)
                    {
                        gridPath = RunAStarPhases(null, gridStartX, gridStartY, startDir,
                                                  gridEndX, gridEndY, endDir, cancellationToken);
                    }
                }
            }

            // Lateral-tolerance retry: the strict phases require an exact
            // on-axis arrival, which is impossible when another waveguide
            // crosses the pin's entry axis outside the cleared corridor.
            // Retry accepting a small lateral offset; the smoother snaps the
            // final approach onto the axis. Only otherwise-blocked routes
            // reach this point, so successful routes are unaffected.
            if (gridPath == null && goalReachable && !cancellationToken.IsCancellationRequested)
            {
                var tolerantRetry = new AStarPathfinder.AStarPathfinder(PathfindingGrid, CostCalculator)
                {
                    MaxNodesExpanded = Phase1MaxNodes,
                    AllowLateralGoalTolerance = true,
                    UseDiagonals = UseDiagonalRouting
                };
                gridPath = tolerantRetry.FindPath(gridStartX, gridStartY, startDir,
                                                  gridEndX, gridEndY, endDir, cancellationToken);
            }

            // Loop detection: if path is >2× Manhattan distance, retry with minimal constraints
            if (gridPath != null && gridPath.Count > gridDistance * 2 && scaledEscape > 2)
            {
                CostCalculator.MinPinEscapeCells = 2;
                CostCalculator.MinStraightRunCells = 2;
                var retry = new AStarPathfinder.AStarPathfinder(PathfindingGrid, CostCalculator)
                {
                    UseDiagonals = UseDiagonalRouting
                };
                var retryPath = retry.FindPath(gridStartX, gridStartY, startDir,
                                               gridEndX, gridEndY, endDir, cancellationToken);
                if (retryPath != null && retryPath.Count < gridPath.Count)
                    gridPath = retryPath;
            }

            if ((gridPath == null || gridPath.Count < 2) && goalReachable)
            {
                CostCalculator.MinPinEscapeCells = 2;
                CostCalculator.MinStraightRunCells = 2;
                var fallback = new AStarPathfinder.AStarPathfinder(PathfindingGrid, CostCalculator)
                {
                    UseDiagonals = UseDiagonalRouting
                };
                gridPath = fallback.FindPath(gridStartX, gridStartY, startDir,
                                             gridEndX, gridEndY, endDir, cancellationToken);
            }

            CostCalculator.MinStraightRunCells = originalStraightRun;

            CostCalculator.MinPinEscapeCells = originalEscapeCells;

            if (gridPath == null || gridPath.Count < 2) return false;

            var smoother = new PathSmoother(PathfindingGrid, bendRadius, AllowedRadiiIncluding(bendRadius));
            var smoothedPath = smoother.ConvertToSegments(gridPath, startPin, endPin);

            path.Segments.AddRange(smoothedPath.Segments);
            path.IsInvalidGeometry = smoothedPath.IsInvalidGeometry;
            path.DebugGridPath = gridPath;

            // Success requires valid segments without geometry violations
            return path.Segments.Count > 0 && !path.IsInvalidGeometry;
        }
        finally
        {
            PathfindingGrid.RestoreCells(clearedStart);
            PathfindingGrid.RestoreCells(clearedEndApproach);
            PathfindingGrid.RestoreCells(clearedEndTerminal);
            PathfindingGrid.RestoreCells(clearedStartFanout);
            PathfindingGrid.RestoreCells(clearedEndFanout);
        }
    }

    /// <summary>
    /// The search window (grid cells) of the most recent bounded A* attempt — a
    /// diagnostic hook for tests pinning that the search space stays bounded to a
    /// corridor around the endpoints instead of spanning the whole chip.
    /// </summary>
    public (int MinX, int MinY, int MaxX, int MaxY)? LastAStarSearchWindow { get; private set; }

    /// <summary>
    /// Minimum margin (grid cells) the search window keeps around the endpoint bounding
    /// box on every side, so a route can dodge obstacles sitting directly on the
    /// pin-to-pin corridor without leaving the window.
    /// </summary>
    private const int SearchWindowMinMarginCells = 40;

    /// <summary>
    /// Runs the A* search for one routing attempt, optionally bounded to
    /// <paramref name="window"/>. One continuous search replaces the former
    /// back-to-back Phase-1/Phase-2 runs: the extended phase's budget IS the total
    /// budget and <see cref="AStarPathfinder.AStarPathfinder.OnEscalationThresholdReached"/>
    /// marks where the quick phase would have ended. The expansion sequence — and
    /// therefore the outcome — is identical to the two-phase version, minus the
    /// repeated quick-phase work and minus the redundant extended re-run when the
    /// quick phase had already emptied the open set (a deterministic re-run of an
    /// exhausted search returns null again).
    /// </summary>
    private List<AStarNode>? RunAStarPhases(
        (int MinX, int MinY, int MaxX, int MaxY)? window,
        int gridStartX, int gridStartY, GridDirection startDir,
        int gridEndX, int gridEndY, GridDirection endDir,
        CancellationToken cancellationToken)
    {
        var search = new AStarPathfinder.AStarPathfinder(PathfindingGrid!, CostCalculator)
        {
            // The continuous search replays the exact expansion sequence of the
            // former quick-then-extended phases (the extended phase re-expanded the
            // quick phase's prefix identically), so the extended phase's budget IS
            // the total budget — the quick phase survives only as the escalation mark.
            MaxNodesExpanded = Phase2MaxNodes,
            UseDiagonals = UseDiagonalRouting,
            SearchWindow = window,
            EscalationThresholdNodes = Phase1MaxNodes,
            OnEscalationThresholdReached = () => OnComplexRouteStarted?.Invoke()
        };
        return search.FindPath(gridStartX, gridStartY, startDir,
                               gridEndX, gridEndY, endDir, cancellationToken);
    }

    /// <summary>
    /// The bounded search window for one wire: the endpoint bounding box inflated by
    /// half its extent per axis and at least <see cref="SearchWindowMinMarginCells"/>
    /// cells, clamped to the grid. Long wires get proportionally more detour room;
    /// short wires search a small local window instead of the whole chip.
    /// </summary>
    private (int MinX, int MinY, int MaxX, int MaxY) ComputeSearchWindow(
        int gridStartX, int gridStartY, int gridEndX, int gridEndY)
    {
        var grid = PathfindingGrid!;
        int marginX = Math.Max(SearchWindowMinMarginCells, Math.Abs(gridEndX - gridStartX) / 2);
        int marginY = Math.Max(SearchWindowMinMarginCells, Math.Abs(gridEndY - gridStartY) / 2);
        return (
            Math.Max(0, Math.Min(gridStartX, gridEndX) - marginX),
            Math.Max(0, Math.Min(gridStartY, gridEndY) - marginY),
            Math.Min(grid.Width - 1, Math.Max(gridStartX, gridEndX) + marginX),
            Math.Min(grid.Height - 1, Math.Max(gridStartY, gridEndY) + marginY));
    }

    /// <summary>
    /// Grid cells a route must run straight from a pin before the first turn, so the first
    /// (or last) arc fits between the pin and the turn apex: the 90° tangent length (= the
    /// bend radius) rounded up past the next whole cell. The extra cell keeps a short
    /// pin-side straight in the smoothed path — the segment-shift handles can collapse it
    /// to exactly zero, putting the bend directly at the pin (pin-lead-stub field finding).
    /// </summary>
    private int PinTangentEscapeCells(double bendRadius) =>
        Math.Max(2, (int)(bendRadius / PathfindingGrid!.CellSizeMicrometers) + 1);

    /// <summary>
    /// The allowed bend radii extended with the current attempt's radius, so the smoother
    /// builds arcs of exactly that radius instead of snapping up to the next foundry value
    /// (which would not match the setbacks the grid path was planned with).
    /// </summary>
    private List<double> AllowedRadiiIncluding(double bendRadius)
    {
        if (AllowedBendRadii.Count == 0 ||
            AllowedBendRadii.Any(r => Math.Abs(r - bendRadius) < RadiusToleranceMicrometers))
            return AllowedBendRadii;

        var radii = new List<double>(AllowedBendRadii) { bendRadius };
        radii.Sort();
        return radii;
    }
}
