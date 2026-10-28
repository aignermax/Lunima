using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>Converts a coarse search result into a path accepted against the original fine grid.</summary>
internal delegate RoutedPath? CoarseRouteAcceptance(PathfindingGrid grid, List<AStarNode> nodes,
    double bendRadius, PhysicalPin startPin, PhysicalPin endPin);

/// <summary>
/// Owns bounded coarse-grid search retries and their cumulative per-wire expansion budget.
/// Candidate geometry acceptance is supplied by the routing policy that owns the fine grid.
/// </summary>
internal sealed class CoarseRouteRetry
{
    internal readonly record struct Settings(int CellSizeFactor, int Phase2MaxNodes,
        int BudgetMultiplier, bool UseDiagonals);

    internal long NodesExpanded { get; private set; }
    internal void Reset() => NodesExpanded = 0;

    internal RoutedPath? TryRoute(PathfindingGrid fineGrid, Settings settings, CoarseRouteAcceptance accept,
        double bendRadius, double startX, double startY, double startAngle,
        double endX, double endY, double endInputAngle, PhysicalPin startPin, PhysicalPin endPin,
        CancellationToken cancellationToken)
    {
        var (path, provedNoPath) = TryAtFactor(
            fineGrid, settings, accept, settings.CellSizeFactor, bendRadius,
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
        // A cancelled search reports neither a path nor a proof, so the token is checked
        // too: otherwise a cancelled route still copies the whole grid a second time.
        if (cancellationToken.IsCancellationRequested
            || NodesExpanded >= MaxNodes(settings)) return null;
        return TryAtFactor(fineGrid, settings, accept, settings.CellSizeFactor * 2, bendRadius,
            startX, startY, startAngle, endX, endY, endInputAngle,
            startPin, endPin, cancellationToken).Path;
    }

    private static int MaxNodes(Settings settings) =>
        (int)Math.Min(int.MaxValue, (long)settings.BudgetMultiplier * settings.Phase2MaxNodes);

    private int CoarseBudget(Settings settings, int factor)
    {
        long factorSquared = (long)settings.Phase2MaxNodes * factor * factor;
        long remaining = MaxNodes(settings) - NodesExpanded;
        return (int)Math.Min(int.MaxValue, Math.Min(factorSquared, Math.Max(0, remaining)));
    }

    private (RoutedPath? Path, bool ProvedNoPath) TryAtFactor(
        PathfindingGrid fineGrid, Settings settings, CoarseRouteAcceptance accept, int factor, double bendRadius,
        double startX, double startY, double startAngle,
        double endX, double endY, double endInputAngle,
        PhysicalPin startPin, PhysicalPin endPin,
        CancellationToken cancellationToken)
    {
        var coarseGrid = fineGrid.CreateCoarseCopy(factor);

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
            coarseGrid, bendRadius, startX, startY, endX, endY, settings.UseDiagonals);

        var (gridStartX, gridStartY) = coarseGrid.PhysicalToGrid(startX, startY);
        var (gridEndX, gridEndY) = coarseGrid.PhysicalToGrid(endX, endY);
        var startDir = GridDirectionExtensions.FromAngle(startAngle);
        var endDir = GridDirectionExtensions.FromAngle(endInputAngle);

        // Budget: the fine Phase-2 budget scaled by factor² — the coarse flood covers
        // factor² fewer cells per expansion, so this keeps the same area coverage the
        // fine search had when it exhausted itself. Saturation bounds the cost: the
        // search ends when the coarse open set empties, long before the budget on
        // small grids.
        int coarseBudget = CoarseBudget(settings, factor);

        var astar = new AStarPathfinder.AStarPathfinder(coarseGrid, costCalculator)
        {
            MaxNodesExpanded = coarseBudget,
            UseDiagonals = settings.UseDiagonals
        };
        var gridPath = astar.FindPath(gridStartX, gridStartY, startDir,
                                      gridEndX, gridEndY, endDir, cancellationToken);
        NodesExpanded += astar.LastSearchNodesExpanded;

        // Same lateral-tolerance retry as the fine attempt: coarse cells quantize the
        // pin's entry axis, so an exact on-axis arrival can be impossible even when a
        // reachably-close cell exists; the smoother snaps the final approach onto the axis.
        // Skipped when the strict run proved no tolerant-goal state is reachable — the
        // retry would re-flood the same exhausted open set and fail identically.
        bool provedNoPath = astar.LastSearchProvedNoPath && !astar.LastSearchReachedGoalVicinity;
        if (gridPath == null && !provedNoPath && !cancellationToken.IsCancellationRequested)
        {
            // Re-derive the budget AFTER the strict run: the per-wire cap
            // is shared between the two, so the retry only gets
            // what the strict attempt left unspent.
            int tolerantBudget = CoarseBudget(settings, factor);
            var tolerantRetry = new AStarPathfinder.AStarPathfinder(coarseGrid, costCalculator)
            {
                MaxNodesExpanded = tolerantBudget,
                AllowLateralGoalTolerance = true,
                UseDiagonals = settings.UseDiagonals
            };
            gridPath = tolerantRetry.FindPath(gridStartX, gridStartY, startDir,
                                              gridEndX, gridEndY, endDir, cancellationToken);
            NodesExpanded += tolerantRetry.LastSearchNodesExpanded;
            provedNoPath = tolerantRetry.LastSearchProvedNoPath;
        }
        if (gridPath == null || gridPath.Count < 2)
        {
            return (null, provedNoPath);
        }

        return (accept(coarseGrid, gridPath, bendRadius, startPin, endPin), false);
    }

    private static RoutingCostCalculator CreateCoarseCostCalculator(
        PathfindingGrid coarseGrid, double bendRadius,
        double startX, double startY, double endX, double endY, bool useDiagonals)
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
            UseDiagonals = useDiagonals,
        };
    }
}
