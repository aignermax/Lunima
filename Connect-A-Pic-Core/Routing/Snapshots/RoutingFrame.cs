using System.Collections.Immutable;
using CAP_Core.Components.Connections;

namespace CAP_Core.Routing.Snapshots;

/// <summary>An immutable ordinary-route frame. Crossing topology is published as one final transaction.</summary>
public sealed class RoutingFrame
{
    private readonly ImmutableArray<ComputedRouteValue> _routes;

    internal RoutingFrame(IEnumerable<WaveguideConnection> connections)
    {
        var wires = connections.ToArray();
        _routes = wires.Select(ComputedRouteValue.Capture).ToImmutableArray();
    }

    internal RoutingFrame(ImmutableArray<ComputedRouteValue> routes) => _routes = routes;
    public int Count => _routes.Length;

    /// <summary>Allocates path objects outside the document commit; no physics models are consulted.</summary>
    public PreparedRoutingFrame Prepare(WaveguideConnectionManager manager)
    {
        var wires = manager.Connections.ToDictionary(wire => wire.Id);
        return new(_routes.Where(value => wires.ContainsKey(value.Id)).Select(value => value.Prepare(wires[value.Id])).ToArray());
    }
}

/// <summary>A frame prepared for one UI commit. It owns fresh paths and has no worker references.</summary>
public sealed class PreparedRoutingFrame
{
    private readonly PreparedRouteValue[] _routes;
    public IReadOnlyList<Guid> ConnectionIds => _routes.Select(route => route.Target.Id).ToArray();

    internal PreparedRoutingFrame(PreparedRouteValue[] routes) => _routes = routes;

    public void Apply()
    {
        foreach (var route in _routes) route.Apply();
    }
}
