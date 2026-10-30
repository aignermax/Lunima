using System.Collections.Immutable;
using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.Snapshots;

internal sealed record RouteRouterValue(double Radius, double ProcessFloor, double MetalFloor,
    ImmutableArray<double> Radii, double Spacing, double CellSize, double Padding, bool Hierarchical,
    bool Direct, bool Diagonal, bool Breakouts, int Phase1, int Phase2, int CoarseFactor, int CoarseBudget,
    ImmutableArray<(double X, double Y, double Width, double Height)>? Regions, RouteGridValue? Grid,
    RouteCostValue Cost, ImmutableArray<RouteObstacleValue> Obstacles, CrossingRouteSettings? Crossings)
{
    internal static RouteRouterValue Capture(WaveguideRouter router, bool preserveCachedGrid = true) => new(router.MinBendRadiusMicrometers,
        router.ProcessMinBendRadiusMicrometers, router.MetalProcessMinBendRadiusMicrometers,
        router.AllowedBendRadii.ToImmutableArray(), router.MinWaveguideSpacingMicrometers,
        router.AStarCellSize, router.ObstaclePaddingMicrometers, router.UseHierarchicalPathfinding,
        router.PreferDirectStyledRoutes, router.UseDiagonalRouting, router.UsePinBreakouts,
        router.Phase1MaxNodes, router.Phase2MaxNodes, router.CoarseRetryCellSizeFactor, router.CoarseRetryBudgetMultiplier,
        router.AllowedRoutingRegions?.ToImmutableArray(), RouteGridValue.Capture(router.PathfindingGrid, preserveCachedGrid),
        RouteCostValue.Capture(router.CostCalculator), router.Obstacles.Select(obstacle => new RouteObstacleValue(
            obstacle.X, obstacle.Y, obstacle.Width, obstacle.Height, obstacle.SourceComponent?.Id)).ToImmutableArray(),
        router.CrossingRouting);

    internal WaveguideRouter Build(IReadOnlyList<Component> roots, IReadOnlyDictionary<Guid, Component> components,
        RouteProcessFloorTable floors)
    {
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = Radius, ProcessMinBendRadiusMicrometers = ProcessFloor,
            MetalProcessMinBendRadiusMicrometers = MetalFloor, AllowedBendRadii = Radii.ToList(),
            MinWaveguideSpacingMicrometers = Spacing, AStarCellSize = CellSize, ObstaclePaddingMicrometers = Padding,
            UseHierarchicalPathfinding = Hierarchical, PreferDirectStyledRoutes = Direct, UseDiagonalRouting = Diagonal,
            UsePinBreakouts = Breakouts, Phase1MaxNodes = Phase1, Phase2MaxNodes = Phase2,
            CoarseRetryCellSizeFactor = CoarseFactor, CoarseRetryBudgetMultiplier = CoarseBudget,
            AllowedRoutingRegions = Regions is { } regions ? regions.ToArray() : null,
            // This closure owns a scalar table only. The caller's provider was evaluated at capture.
            ConnectionProcessFloorProvider = floors.Resolve,
            CrossingRouting = Crossings
        };
        foreach (var obstacle in Obstacles) router.Obstacles.Add(new RoutingObstacle
        {
            X = obstacle.X, Y = obstacle.Y, Width = obstacle.Width, Height = obstacle.Height,
            SourceComponent = obstacle.Component is { } id ? components[id] : null
        });
        Grid?.Initialize(router, roots);
        Cost.Apply(router.CostCalculator);
        return router;
    }
}

internal readonly record struct RouteObstacleValue(double X, double Y, double Width, double Height, Guid? Component);

internal sealed record RouteGridValue(double MinX, double MinY, double MaxX, double MaxY,
    double CellSize, double Padding, int Width, int Height, ImmutableArray<byte> Cells,
    ImmutableArray<OutlineRasterEntry> Rasters = default)
{
    internal static RouteGridValue? Capture(PathfindingGrid? grid, bool preserveCells) => grid == null ? null : new(
        grid.MinX, grid.MinY, grid.MaxX, grid.MaxY, grid.CellSizeMicrometers, grid.ObstaclePaddingMicrometers,
        grid.Width, grid.Height, preserveCells ? grid.CaptureCellStates().ToImmutableArray() : default, grid.CaptureOutlineRasters());

    internal void Initialize(WaveguideRouter router, IReadOnlyList<Component> components)
    {
        router.InitializePathfindingGrid(MinX, MinY, MaxX, MaxY, components, CellSize, Rasters);
        var grid = router.PathfindingGrid!;
        if (grid.ObstaclePaddingMicrometers != Padding)
        {
            grid.ObstaclePaddingMicrometers = Padding;
            grid.RebuildFromComponents(components);
        }
        if (Cells.IsDefault) return;
        if (grid.Width != Width || grid.Height != Height) throw new InvalidDataException("Captured routing grid dimensions changed.");
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
        {
            var state = Cells[x * Height + y];
            // Existing live waveguides are rebuilt by the private manager. Preserve hand-set
            // fences/openings and other non-waveguide state just like the routing clone path.
            if (state != 2 && grid.GetCellState(x, y) != state) grid.SetCellState(x, y, state);
        }
    }
}

internal sealed record RouteCostValue(double Straight, double Turn, double Radius, int StraightRun, int Escape,
    double CellSize, double SafeSpacing, double Proximity, double PinPenalty, bool Diagonal)
{
    internal static RouteCostValue Capture(RoutingCostCalculator cost) => new(cost.StraightCostPerMicrometer,
        cost.TurnCostPer90Degrees, cost.MinBendRadiusMicrometers, cost.MinStraightRunCells, cost.MinPinEscapeCells,
        cost.CellSizeMicrometers, cost.MinSafeSpacingMicrometers, cost.ProximityCostMultiplier, cost.PinZoneCostPenalty, cost.UseDiagonals);
    internal void Apply(RoutingCostCalculator cost)
    {
        cost.StraightCostPerMicrometer = Straight;
        cost.TurnCostPer90Degrees = Turn;
        cost.MinBendRadiusMicrometers = Radius;
        cost.MinStraightRunCells = StraightRun;
        cost.MinPinEscapeCells = Escape;
        cost.CellSizeMicrometers = CellSize;
        cost.MinSafeSpacingMicrometers = SafeSpacing;
        cost.ProximityCostMultiplier = Proximity;
        cost.PinZoneCostPenalty = PinPenalty;
        cost.UseDiagonals = Diagonal;
    }
}
