using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Routing.Snapshots;

internal sealed record RouteCrossingValue(bool Insert, CrossingPrototypeValue? Prototype,
    TimeSpan Budget, int Maximum, ImmutableArray<(int MaxNodesExpanded, double HeuristicWeight)> Searches,
    ImmutableArray<RouteCrossingRecordValue> Records, ImmutableDictionary<(Guid, Guid), double> Floors,
    ImmutableArray<Guid> PendingReroutes)
{
    internal static RouteCrossingValue? Capture(WaveguideConnectionManager manager, WaveguideRouter router,
        IEnumerable<Component> components)
    {
        var service = manager.CrossingService;
        if (service == null) return null;
        // Resolve at most one PDK instance while still on the owning thread. Maintenance needs no PDK.
        var prototype = manager.CrossingInsertion == null ? null : service.CrossingComponentFactory();
        return new(manager.CrossingInsertion != null, prototype == null ? null : CrossingPrototypeValue.Capture(prototype),
            service.ChainPassTimeBudget, service.MaxCrossingsPerPass, service.FallbackChainSearches.ToImmutableArray(),
            service.Records.Select(RouteCrossingRecordValue.Capture).ToImmutableArray(), prototype == null
                ? ImmutableDictionary<(Guid, Guid), double>.Empty
                : RouteProcessFloorTable.CapturePrototypePairs(prototype, components, router),
            manager.CapturePendingReroutes());
    }

    internal void Attach(RoutingWorkGraph graph, IReadOnlyDictionary<Guid, Component> components,
        IReadOnlyDictionary<Guid, WaveguideConnection> wires, RouteProcessFloorTable floors)
    {
        var service = new CrossingInsertionService(() => Prototype?.Build(floors))
        {
            ChainPassTimeBudget = Budget, MaxCrossingsPerPass = Maximum, FallbackChainSearches = Searches,
            ComponentAdded = graph.AddCrossing, ComponentRemoved = graph.RemoveCrossing,
            ChangeRecorded = graph.RecordCrossingChange
        };
        foreach (var record in Records) service.RestoreRecord(record.Build(components, wires));
        if (Insert) graph.Manager.CrossingInsertion = service;
        else graph.Manager.CrossingMaintenance = service;
        graph.Manager.RestorePendingReroutes(PendingReroutes.Where(wires.ContainsKey).Select(id => wires[id]));
    }
}

internal sealed record RouteCrossingRecordValue(Guid Component, Guid OriginalA, Guid OriginalB,
    ImmutableArray<Guid> PiecesA, ImmutableArray<Guid> PiecesB, ImmutableArray<(double X, double Y)> Anchors)
{
    internal static RouteCrossingRecordValue Capture(CrossingRecord record) => new(record.CrossingComponent.Id,
        record.OriginalA.Id, record.OriginalB.Id, record.SubConnectionsA.Select(wire => wire.Id).ToImmutableArray(),
        record.SubConnectionsB.Select(wire => wire.Id).ToImmutableArray(), record.AnchorPositions.ToImmutableArray());

    internal CrossingRecord Build(IReadOnlyDictionary<Guid, Component> components,
        IReadOnlyDictionary<Guid, WaveguideConnection> wires) => new()
    {
        CrossingComponent = components[Component], OriginalA = wires[OriginalA], OriginalB = wires[OriginalB],
        SubConnectionsA = PiecesA.Select(id => wires[id]).ToList(), SubConnectionsB = PiecesB.Select(id => wires[id]).ToList(),
        AnchorPositions = Anchors.ToList()
    };
}
