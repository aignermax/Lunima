namespace CAP_Core.Routing;

/// <summary>
/// Isolated-clone support for <see cref="WaveguideRouter"/>: a fully independent router whose
/// pathfinding grid reproduces this router's component-side obstacle state, for routing
/// attempts that must not share mutable state (split out to keep the router below the
/// file-size limit).
/// </summary>
public partial class WaveguideRouter
{
    /// <summary>
    /// Builds an independent router with the same routing settings and a fresh pathfinding
    /// grid rebuilt from the components registered on this router's grid. A route computed on
    /// the clone sees exactly the obstacle state this router's grid would present with no
    /// waveguides registered, so the clone produces the same path a fresh full re-route on
    /// this router would — while this router and its grid stay untouched.
    /// Hierarchical pathfinding is not cloned (the sector graph is expensive to build and
    /// binds to the source grid); callers that need HPA must route on the original router.
    /// </summary>
    internal WaveguideRouter CreateIsolatedRoutingClone()
    {
        var grid = PathfindingGrid
            ?? throw new InvalidOperationException("a routing clone needs the source router's pathfinding grid");
        var clone = new WaveguideRouter
        {
            MinBendRadiusMicrometers = MinBendRadiusMicrometers,
            ProcessMinBendRadiusMicrometers = ProcessMinBendRadiusMicrometers,
            MetalProcessMinBendRadiusMicrometers = MetalProcessMinBendRadiusMicrometers,
            AllowedBendRadii = AllowedBendRadii,
            MinWaveguideSpacingMicrometers = MinWaveguideSpacingMicrometers,
            AStarCellSize = AStarCellSize,
            // The grid's padding is authoritative: the canvas overrides it after initialization.
            ObstaclePaddingMicrometers = grid.ObstaclePaddingMicrometers,
            UseDiagonalRouting = UseDiagonalRouting,
            PreferDirectStyledRoutes = PreferDirectStyledRoutes,
            Phase1MaxNodes = Phase1MaxNodes,
            Phase2MaxNodes = Phase2MaxNodes,
            ConnectionProcessFloorProvider = ConnectionProcessFloorProvider,
            OnComplexRouteStarted = OnComplexRouteStarted,
        };
        clone.Obstacles.AddRange(Obstacles);
        clone.InitializePathfindingGrid(grid.MinX, grid.MinY, grid.MaxX, grid.MaxY,
            grid.SnapshotObstacleComponents(), grid.CellSizeMicrometers);
        return clone;
    }
}
