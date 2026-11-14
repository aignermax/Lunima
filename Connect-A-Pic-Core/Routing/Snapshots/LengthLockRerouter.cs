using CAP_Core.Components.Connections;
using CAP_Core.Routing.LengthEqualization;

namespace CAP_Core.Routing.Snapshots;

/// <summary>
/// Lays wires out at one locked length when a bundle's nested arches cannot reach it — the way
/// "Equalize lengths" does: the general router routes every wire afresh, then a meander folds each
/// route up to the length, the largest radius first. Everything happens on a private copy of the
/// design, so it may run off the UI thread and the live design stays untouched; the caller
/// installs the routes.
/// </summary>
public static class LengthLockRerouter
{
    /// <summary>
    /// Routes the wires named by <paramref name="wireIds"/> with the general router and stretches
    /// each to <paramref name="targetMicrometers"/>.
    /// </summary>
    /// <param name="design">The design captured on its owning thread.</param>
    /// <param name="wireIds">The wires to lay out at the length; the rest of the design are obstacles.</param>
    /// <param name="targetMicrometers">The locked length.</param>
    /// <param name="toleranceMicrometers">Accepted deviation from it.</param>
    /// <param name="maxRadiusMicrometers">Largest meander radius to try.</param>
    /// <param name="minRadiusMicrometers">Smallest meander radius to allow, never below the process minimum.</param>
    /// <param name="cancellation">Stops the routing and the meander search.</param>
    public static LengthLockRerouteResult Solve(RoutingInputSnapshot design, IReadOnlyCollection<Guid> wireIds,
        double targetMicrometers, double toleranceMicrometers, double maxRadiusMicrometers, double minRadiusMicrometers,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(wireIds);
        var graph = design.CreatePrivateGraph();
        var wires = RouteAfresh(graph, wireIds, cancellation);
        var plan = new LengthEqualizer(graph.Router).Prepare(wires, graph.Manager.Connections.ToList(),
            graph.Components, targetMicrometers, maxRadiusMicrometers, toleranceMicrometers,
            minRadiusMicrometers: minRadiusMicrometers);
        var result = LengthEqualizer.Solve(plan, cancellation);

        var matched = new Dictionary<Guid, RoutedPath>();
        var missed = new Dictionary<Guid, RoutedPath>();
        foreach (var outcome in result.Outcomes)
        {
            if (outcome.Path != null)
                matched[outcome.Connection.Id] = outcome.Path;
            else if (outcome.BasePath != null)
                missed[outcome.Connection.Id] = outcome.BasePath;
        }
        return new LengthLockRerouteResult(matched, missed, wires.Count);
    }

    /// <summary>
    /// Drops the wires' current routes — whatever style they had — and lets the general router
    /// find new ones as for a fresh connection. The radius a lock records is its meander's loop
    /// radius, not a routing preference: routing with it (up to 250 µm) turns short diagonal
    /// routes into long axis-parallel ones that no meander can bring back down to the lock. The
    /// router still never bends tighter than the process minimum.
    /// </summary>
    private static List<WaveguideConnection> RouteAfresh(RoutingWorkGraph graph, IReadOnlyCollection<Guid> wireIds,
        CancellationToken cancellation)
    {
        var ids = wireIds.ToHashSet();
        var wires = graph.Manager.Connections.Where(c => ids.Contains(c.Id)).ToList();
        foreach (var wire in wires)
        {
            wire.Type = WaveguideType.Auto;
            wire.IsRouteFrozen = false;
            wire.BendRadiusMicrometers = WaveguideConnection.DefaultBendRadiusMicrometers;
            wire.InvalidateRoute();
        }
        graph.Route(cancellation);
        return wires;
    }
}

/// <summary>The outcome of <see cref="LengthLockRerouter.Solve"/>, keyed by connection id.</summary>
/// <param name="Matched">Routes at the locked length.</param>
/// <param name="Missed">The router's routes of wires no meander brought to the length; off the lock.</param>
/// <param name="WireCount">How many of the requested wires the design contains.</param>
public sealed record LengthLockRerouteResult(
    IReadOnlyDictionary<Guid, RoutedPath> Matched,
    IReadOnlyDictionary<Guid, RoutedPath> Missed,
    int WireCount)
{
    /// <summary>True when every requested wire has a route at the locked length.</summary>
    public bool AllMatched => WireCount > 0 && Matched.Count == WireCount;
}
