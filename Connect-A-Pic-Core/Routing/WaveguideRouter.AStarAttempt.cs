using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Routing;

/// <summary>
/// Adapts a bend-radius attempt to the fine-grid search policy, owning temporary pin corridors,
/// cost restoration, smoothing and final crossing-aware geometric acceptance.
/// </summary>
public partial class WaveguideRouter
{
    private readonly FineGridRouteSearch _fineSearch = new();

    /// <summary>
    /// The reachability verdict of the most recent <see cref="TryRouteAStar"/> call. When
    /// false, the whole-grid flood proved no path to the goal region can exist on the fine
    /// grid (pin corridors already cleared) — the coarse-grid retry is then futile too:
    /// its blocked set is a superset of the fine grid's, and a route through the coarser
    /// corridor quantization would not survive the fine-grid collision validation anyway.
    /// </summary>
    private bool _lastFineAttemptGoalReachable;

    /// <summary>
    /// Attempts to route using A* pathfinding with obstacle avoidance at the given
    /// bend radius. The cost model (minimum straight run before turns) and the path smoother
    /// are synced to that radius, so the grid path leaves room for the arcs that will be built.
    /// Before any search runs, a direction-aware reachability flood
    /// (<see cref="PathfindingGrid.CanReachGoalDirected"/>) decides whether a path can exist
    /// at all — a proven-unreachable goal skips every search below with the same null result
    /// they would return after burning their full node budgets. The search runs over the
    /// whole grid with the extended node budget (<see cref="Phase2MaxNodes"/>), firing
    /// <see cref="OnComplexRouteStarted"/> at the quick-phase mark (<see cref="Phase1MaxNodes"/>).
    /// </summary>
    private bool TryRouteAStar(double bendRadius,
                                double startX, double startY, double startAngle,
                                double endX, double endY, double endInputAngle,
                                RoutedPath path, PhysicalPin startPin, PhysicalPin endPin,
                                CancellationToken cancellationToken = default)
    {
        if (PathfindingGrid == null) return false;

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

            var crossingStep = CreateCrossingStep();
            LastPlannedCrossings = Array.Empty<PlannedCrossing>();

            // The heuristic's distance metric must match the movement model.
            CostCalculator.UseDiagonals = UseDiagonalRouting;

            var gridPath = _fineSearch.FindPath(PathfindingGrid, CostCalculator,
                UseHierarchicalPathfinding ? _hierarchicalPathfinder : null, crossingStep,
                crossingStep != null ? CrossingRouting?.MaxNodesExpanded : null,
                gridStartX, gridStartY, startDir, gridEndX, gridEndY, endDir, scaledEscape,
                cancellationToken, out _lastFineAttemptGoalReachable);

            CostCalculator.MinStraightRunCells = originalStraightRun;

            CostCalculator.MinPinEscapeCells = originalEscapeCells;

            if (gridPath == null || gridPath.Count < 2) return false;

            var smoother = new PathSmoother(PathfindingGrid, bendRadius, AllowedRadiiIncluding(bendRadius));
            var smoothedPath = smoother.ConvertToSegments(gridPath, startPin, endPin);

            path.Segments.AddRange(smoothedPath.Segments);
            path.IsInvalidGeometry = smoothedPath.IsInvalidGeometry;
            path.DebugGridPath = gridPath;
            RecordPlannedCrossings(gridPath);

            // Success requires valid segments without geometry violations
            if (path.Segments.Count == 0 || path.IsInvalidGeometry
                || CutsThroughClearedWaveguide(path, (startX, startY), (endX, endY), clearedStart,
                    clearedEndApproach, clearedEndTerminal, clearedStartFanout, clearedEndFanout))
            {
                // A failed attempt's crossings must not leak into the route a later attempt finds.
                ResetPlannedCrossings();
                path.Segments.Clear();
                return false;
            }
            return true;
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
