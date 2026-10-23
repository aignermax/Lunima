using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Immutable calculated values plus exclusive ownership of a completed grid. Prepare consumes the grid once.</summary>
public sealed class RoutingPassResult
{
    private readonly ImmutableArray<RouteConnectionValue> _definitions;
    private readonly ImmutableArray<ComputedRouteValue> _routes;
    private readonly ImmutableArray<Guid> _order;
    private readonly ImmutableArray<CrossingPrototypeValue> _newCrossings;
    private readonly ImmutableArray<Guid> _componentIds;
    private readonly ImmutableArray<RouteCrossingRecordValue> _records;
    private readonly ImmutableDictionary<Guid, Guid> _modelOrigins;
    private readonly ImmutableHashSet<Guid> _unchangedPaths;
    private readonly RouteManagerValue _manager;
    private readonly RouteCostValue _cost;
    private readonly ImmutableArray<RouteObstacleValue> _obstacles;
    private readonly ImmutableArray<(double X, double Y, double Width, double Height)>? _regions;
    private readonly ImmutableArray<Guid> _pendingReroutes;
    private readonly bool _calculateRoutes;
    private readonly RoutingPassTimings? _timings;
    private PathfindingGrid? _grid;
    private int _prepared;

    private RoutingPassResult(RoutingWorkGraph graph, HashSet<Guid> originalIds,
        IReadOnlyDictionary<IDispersionModel, Guid> models, bool calculateRoutes, IEnumerable<Guid> unchangedPaths)
    {
        var records = graph.Manager.CrossingService?.Records;
        var wires = AllWires(graph.Manager).ToArray();
        _definitions = wires.Select(wire => RouteConnectionValue.Capture(wire, graph.Router)).ToImmutableArray();
        _routes = wires.Select(ComputedRouteValue.Capture).ToImmutableArray();
        _unchangedPaths = unchangedPaths.ToImmutableHashSet();
        _order = graph.Manager.Connections.Select(wire => wire.Id).ToImmutableArray();
        _newCrossings = graph.Components.Where(component => !originalIds.Contains(component.Id))
            .Select(CrossingPrototypeValue.Capture).ToImmutableArray();
        _componentIds = graph.Components.Select(component => component.Id).ToImmutableArray();
        _records = records?.Select(RouteCrossingRecordValue.Capture).ToImmutableArray() ?? [];
        _modelOrigins = wires.Where(wire => wire.DispersionModel != null && models.ContainsKey(wire.DispersionModel))
            .ToImmutableDictionary(wire => wire.Id, wire => models[wire.DispersionModel!]);
        _manager = RouteManagerValue.Capture(graph.Manager);
        _cost = RouteCostValue.Capture(graph.Router.CostCalculator);
        _obstacles = graph.Router.Obstacles.Select(obstacle => new RouteObstacleValue(obstacle.X, obstacle.Y,
            obstacle.Width, obstacle.Height, obstacle.SourceComponent?.Id)).ToImmutableArray();
        _regions = graph.Router.AllowedRoutingRegions?.ToImmutableArray();
        _pendingReroutes = graph.Manager.CapturePendingReroutes();
        _grid = graph.Router.PathfindingGrid;
        _calculateRoutes = calculateRoutes;
        _timings = graph.Manager.LastRoutingPassTimings;
    }

    internal static RoutingPassResult Capture(RoutingWorkGraph graph, HashSet<Guid> originalIds,
        IReadOnlyDictionary<IDispersionModel, Guid> models, bool calculateRoutes, IEnumerable<Guid>? unchangedPaths = null) =>
        new(graph, originalIds, models, calculateRoutes, unchangedPaths ?? []);

    internal static Dictionary<IDispersionModel, Guid> CaptureModelOrigins(WaveguideConnectionManager manager)
    {
        var origins = new Dictionary<IDispersionModel, Guid>(ReferenceEqualityComparer.Instance);
        foreach (var wire in AllWires(manager))
            if (wire.DispersionModel != null) origins.TryAdd(wire.DispersionModel, wire.Id);
        return origins;
    }

    internal static IEnumerable<WaveguideConnection> AllWires(WaveguideConnectionManager manager) =>
        manager.Connections.Concat(manager.CrossingService?.Records.SelectMany(record =>
            new[] { record.OriginalA, record.OriginalB }.Concat(record.AllSubConnections)) ?? [])
            .DistinctBy(wire => wire.Id);

    /// <summary>Publication-side identity guard. Keep this map on the editor thread, outside worker inputs.</summary>
    public static IReadOnlyDictionary<Guid, RoutedPath?> CaptureOriginalPaths(WaveguideConnectionManager manager) =>
        AllWires(manager).ToDictionary(wire => wire.Id, wire => wire.RoutedPath);

    /// <summary>Remaps accepted model identities and allocates paths before entering a short document commit.</summary>
    public PreparedRoutingCommit Prepare(IReadOnlyList<Component> currentRoots, WaveguideConnectionManager manager,
        WaveguideRouter router, IReadOnlyDictionary<Guid, RoutedPath?>? originalPaths = null)
    {
        if (Interlocked.Exchange(ref _prepared, 1) != 0) throw new InvalidOperationException("Routing result already prepared.");
        var components = CollectComponents(currentRoots, manager);
        var oldWires = AllWires(manager).ToDictionary(wire => wire.Id);
        var added = _newCrossings.Select(value => value.Build(new RouteProcessFloorTable([], null), preserveIdentity: true)).ToArray();
        foreach (var component in added) components.Add(component.Id, component);
        var pins = components.Values.SelectMany(component => component.PhysicalPins).ToDictionary(pin => pin.PinId);
        var wires = _definitions.ToDictionary(value => value.Id, value => oldWires.TryGetValue(value.Id, out var existing)
            ? existing : value.Build(pins, restoreRoute: false));
        foreach (var wire in wires.Values.Where(wire => !oldWires.ContainsKey(wire.Id)))
        {
            // Existing wires keep their live wavelength models. New split pieces inherit their original model.
            wire.DispersionModel = _modelOrigins.TryGetValue(wire.Id, out var original) && oldWires.TryGetValue(original, out var source)
                ? source.DispersionModel : wire.StartPin.ParentComponent.WaveguideDispersion ?? wire.EndPin.ParentComponent.WaveguideDispersion;
        }
        var removed = currentRoots.Where(component => component.IsInsertedCrossing && !_componentIds.Contains(component.Id)).ToArray();
        var before = added.Length != 0 || removed.Length != 0 ? RoutingTopologyBackup.Capture(manager, router) : null;
        var grid = Interlocked.Exchange(ref _grid, null);
        grid?.RebindOwners(components);
        var records = _records.Select(record => record.Build(components, wires)).ToArray();
        var obstacles = _obstacles.Select(value => new RoutingObstacle { X = value.X, Y = value.Y,
            Width = value.Width, Height = value.Height, SourceComponent = value.Component is { } id ? components[id] : null }).ToArray();
        // Original editor references remain on the publication side; they never enter the worker input.
        // A progressive frame may have replaced a route before a worker rollback restores its initial private path.
        bool PreservePath(Guid id) => _unchangedPaths.Contains(id) && originalPaths != null
            && originalPaths.TryGetValue(id, out var original) && ReferenceEquals(wires[id].RoutedPath, original);
        return new(manager, router, _routes.Select(value => value.Prepare(wires[value.Id], PreservePath(value.Id))).ToArray(),
            _order.Select(id => wires[id]).ToArray(), records, added, removed, before, grid, _regions, _cost, obstacles,
            _manager, _pendingReroutes.Where(wires.ContainsKey).Select(id => wires[id]).ToArray(), _calculateRoutes, _timings);
    }

    private static Dictionary<Guid, Component> CollectComponents(IEnumerable<Component> roots, WaveguideConnectionManager manager)
    {
        var components = new Dictionary<Guid, Component>();
        void Visit(Component component)
        {
            if (!components.TryAdd(component.Id, component)) return;
            if (component is ComponentGroup group) foreach (var child in group.ChildComponents) Visit(child);
            if (component.ParentGroup is ComponentGroup parent) Visit(parent);
        }
        foreach (var component in roots) Visit(component);
        foreach (var wire in AllWires(manager)) { Visit(wire.StartPin.ParentComponent); Visit(wire.EndPin.ParentComponent); }
        return components;
    }
}
