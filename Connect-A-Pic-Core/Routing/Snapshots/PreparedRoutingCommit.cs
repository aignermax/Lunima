using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Prepared values for one atomic routing transaction. Applying performs no routing, model calls or rasterization.</summary>
public sealed class PreparedRoutingCommit
{
    private readonly WaveguideConnectionManager _manager;
    private readonly WaveguideRouter _router;
    private readonly PreparedRouteValue[] _routes;
    private readonly WaveguideConnection[] _order;
    private readonly CrossingRecord[] _records;
    private readonly PathfindingGrid? _grid;
    private readonly ImmutableArray<(double X, double Y, double Width, double Height)>? _regions;
    private readonly RouteCostValue _cost;
    private readonly RoutingObstacle[] _obstacles;
    private readonly RouteManagerValue _state;
    private readonly WaveguideConnection[] _pending;
    private readonly bool _calculateRoutes;
    private readonly RoutingPassTimings? _timings;
    public IReadOnlyList<Component> AddedCrossings { get; }
    public IReadOnlyList<Component> RemovedCrossings { get; }
    public CrossingChange? CrossingChange { get; }

    internal PreparedRoutingCommit(WaveguideConnectionManager manager, WaveguideRouter router, PreparedRouteValue[] routes,
        WaveguideConnection[] order, CrossingRecord[] records, Component[] added, Component[] removed,
        RoutingTopologyBackup? before, PathfindingGrid? grid,
        ImmutableArray<(double X, double Y, double Width, double Height)>? regions, RouteCostValue cost,
        RoutingObstacle[] obstacles, RouteManagerValue state, WaveguideConnection[] pending, bool calculateRoutes,
        RoutingPassTimings? timings)
    {
        _manager = manager; _router = router; _routes = routes; _order = order; _records = records;
        _grid = grid; _regions = regions; _cost = cost; _obstacles = obstacles;
        _state = state; _pending = pending; _calculateRoutes = calculateRoutes;
        _timings = timings;
        AddedCrossings = added; RemovedCrossings = removed;
        if (before != null)
            CrossingChange = new(added, removed, order, () => before.Restore(_manager, _router));
    }

    public void Apply()
    {
        if (_calculateRoutes)
        {
            foreach (var route in _routes) route.Apply();
            lock (_manager.SyncRoot)
            {
                _manager.Connections.Clear();
                _manager.Connections.AddRange(_order);
            }
            _state.RestoreFailures(_manager);
            _manager.RestorePendingReroutes(_pending);
            _manager.ApplyCompletedDiagnostics(_timings);
            if (_manager.CrossingService is { } service)
            {
                service.Reset();
                foreach (var record in _records) service.RestoreRecord(record);
            }
        }
        _router.AdoptCompletedGrid(_grid, _regions);
        _router.Obstacles.Clear();
        _router.Obstacles.AddRange(_obstacles);
        _cost.Apply(_router.CostCalculator);
    }
}

/// <summary>UI-owned undo state captured before publication; no worker closure or model clone is retained.</summary>
internal sealed record RoutingTopologyBackup(WaveguideConnection[] Order, PreparedRouteValue[] Routes,
    CrossingRecord[] Records, PathfindingGrid? Grid,
    IReadOnlyList<(double X, double Y, double Width, double Height)>? Regions,
    RouteCostValue Cost, RoutingObstacle[] Obstacles, RouteManagerValue State, WaveguideConnection[] Pending)
{
    internal static RoutingTopologyBackup Capture(WaveguideConnectionManager manager, WaveguideRouter router) => new(
        manager.Connections.ToArray(), RoutingPassResult.AllWires(manager).Select(wire => ComputedRouteValue.Capture(wire).Prepare(wire)).ToArray(),
        manager.CrossingService?.Records.ToArray() ?? [], router.PathfindingGrid, router.AllowedRoutingRegions?.ToArray(),
        RouteCostValue.Capture(router.CostCalculator), router.Obstacles.ToArray(), RouteManagerValue.Capture(manager),
        manager.Connections.Where(wire => manager.CapturePendingReroutes().Contains(wire.Id)).ToArray());

    internal void Restore(WaveguideConnectionManager manager, WaveguideRouter router)
    {
        foreach (var route in Routes) route.Apply();
        lock (manager.SyncRoot) { manager.Connections.Clear(); manager.Connections.AddRange(Order); }
        State.RestoreFailures(manager);
        manager.RestorePendingReroutes(Pending);
        if (manager.CrossingService is { } service)
        {
            service.Reset();
            foreach (var record in Records) service.RestoreRecord(record);
        }
        router.AdoptCompletedGrid(Grid, Regions);
        router.Obstacles.Clear(); router.Obstacles.AddRange(Obstacles);
        Cost.Apply(router.CostCalculator);
    }
}
