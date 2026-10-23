using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.BendLoss;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Immutable inputs for an identity-preserving, private routing pass.</summary>
public sealed class RoutingInputSnapshot
{
    private readonly ImmutableArray<Guid> _rootIds;
    private readonly ImmutableArray<RouteComponentValue> _components;
    private readonly ImmutableArray<RouteConnectionValue> _connections;
    private readonly ImmutableArray<Guid> _connectionOrder;
    private readonly RouteRouterValue _router;
    private readonly RouteManagerValue _manager;
    private readonly RouteCrossingValue? _crossings;

    private RoutingInputSnapshot(ImmutableArray<Guid> rootIds, ImmutableArray<RouteComponentValue> components,
        ImmutableArray<RouteConnectionValue> connections, ImmutableArray<Guid> connectionOrder,
        RouteRouterValue router, RouteManagerValue manager, RouteCrossingValue? crossings)
    {
        _rootIds = rootIds;
        _components = components;
        _connections = connections;
        _connectionOrder = connectionOrder;
        _router = router;
        _manager = manager;
        _crossings = crossings;
    }

    /// <summary>
    /// Captures a quiescent routing graph on its owning thread. Callers must drain any earlier
    /// live routing pass before capture. Physics values use the router's existing 1550 nm loss reference.
    /// Crossing prototypes are resolved once here; factories, notifications and undo callbacks never cross the boundary.
    /// </summary>
    public static RoutingInputSnapshot Capture(IReadOnlyList<Component> components,
        WaveguideRouter router, WaveguideConnectionManager manager)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(manager);
        lock (manager.SyncRoot)
        {
            var records = manager.CrossingService?.Records;
            var wires = manager.Connections.Concat(records?.SelectMany(record =>
                new[] { record.OriginalA, record.OriginalB }.Concat(record.AllSubConnections))
                ?? Enumerable.Empty<WaveguideConnection>()).DistinctBy(wire => wire.Id).ToArray();
            var originals = new Dictionary<Guid, Component>();
            var active = new HashSet<Guid>();
            void Visit(Component component)
            {
                if (active.Contains(component.Id)) throw new InvalidDataException("Routing hierarchy contains a cycle.");
                if (originals.TryGetValue(component.Id, out var known))
                {
                    if (!ReferenceEquals(known, component)) throw new InvalidDataException("Duplicate routing component identity.");
                    return;
                }
                originals.Add(component.Id, component);
                active.Add(component.Id);
                if (component is ComponentGroup group)
                    foreach (var child in group.ChildComponents) Visit(child);
                active.Remove(component.Id);
            }
            foreach (var component in components) Visit(component);
            if (records != null) foreach (var record in records) Visit(record.CrossingComponent);
            foreach (var connection in wires)
            {
                if (connection.StartPin?.ParentComponent is { } start) Visit(start);
                if (connection.EndPin?.ParentComponent is { } end) Visit(end);
            }
            foreach (var obstacle in router.Obstacles)
                if (obstacle.SourceComponent is { } component) Visit(component);
            // Visible sub-canvas children retain their private parent identity for facet semantics.
            foreach (var component in originals.Values.ToArray())
            {
                var ancestors = new HashSet<Guid>();
                var parent = component.ParentGroup as ComponentGroup;
                while (parent != null)
                {
                    if (!ancestors.Add(parent.Id)) throw new InvalidDataException("Routing parent hierarchy contains a cycle.");
                    Visit(parent);
                    parent = parent.ParentGroup as ComponentGroup;
                }
            }
            return new(components.Select(component => component.Id).ToImmutableArray(),
                originals.Values.Select(RouteComponentValue.Capture).ToImmutableArray(),
                wires.Select(connection => RouteConnectionValue.Capture(connection, router)).ToImmutableArray(),
                manager.Connections.Select(connection => connection.Id).ToImmutableArray(),
                RouteRouterValue.Capture(router), RouteManagerValue.Capture(manager),
                RouteCrossingValue.Capture(manager, router, originals.Values));
        }
    }

    /// <summary>Creates fresh mutable routing objects; repeated calls share only immutable captured values.</summary>
    public RoutingWorkGraph CreatePrivateGraph()
    {
        var pins = new Dictionary<Guid, PhysicalPin>();
        var logicalPins = new Dictionary<Guid, Pin>();
        var components = _components.ToDictionary(value => value.Id, value => value.BuildShell(pins, logicalPins));
        foreach (var value in _components) value.AttachHierarchy(components[value.Id], components, pins);
        foreach (var value in _components) value.ApplyGeometry(components[value.Id]);
        var roots = _rootIds.Select(id => components[id]).ToArray();
        var floors = new RouteProcessFloorTable(_connections, _crossings?.Floors);
        var router = _router.Build(roots, components, floors);
        var manager = _manager.Build(router);
        var wires = _connections.ToDictionary(value => value.Id, value => value.Build(pins));
        foreach (var id in _connectionOrder) manager.AddExistingConnection(wires[id]);
        manager.RestoreFailureState(_manager.Failures, _manager.PendingWireSetChange);
        var graph = new RoutingWorkGraph(roots, router, manager);
        _crossings?.Attach(graph, components, wires, floors);
        return graph;
    }
}

internal sealed record RouteManagerValue(bool Sequential, int Attempts, double WaveguideWidth, double MetalWidth,
    double DefaultPropagationLoss, double DefaultExcessLoss, BendLossModel DefaultBendLoss, bool Coherent,
    bool PendingWireSetChange, ImmutableArray<UnroutableWireMemory.SnapshotEntry> Failures)
{
    internal static RouteManagerValue Capture(WaveguideConnectionManager manager) => new(manager.UseSequentialRouting,
        manager.MaxRoutingAttempts, manager.WaveguideWidthMicrometers, manager.MetalTraceWidthMicrometers,
        manager.DefaultPropagationLossDbPerCm, manager.DefaultExcessBendLossDbPer90Deg, manager.DefaultBendLossModel,
        manager.EnableCoherentPropagationPhase, manager.HasPendingWireSetChange, manager.UnroutableWires.CaptureState());

    internal WaveguideConnectionManager Build(WaveguideRouter router) => new(router)
    {
        UseSequentialRouting = Sequential, MaxRoutingAttempts = Attempts, WaveguideWidthMicrometers = WaveguideWidth,
        MetalTraceWidthMicrometers = MetalWidth, DefaultPropagationLossDbPerCm = DefaultPropagationLoss,
        DefaultExcessBendLossDbPer90Deg = DefaultExcessLoss, DefaultBendLossModel = DefaultBendLoss,
        EnableCoherentPropagationPhase = Coherent
    };
}
