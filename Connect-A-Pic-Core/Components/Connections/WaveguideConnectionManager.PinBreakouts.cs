using CAP_Core.Routing.PinBreakout;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Breakout planning at the start of a routing pass: the tight pin clusters among the
/// auto-routed optical wires get their breakout S-bends and leads planned and reserved on
/// the grid before any wire routes, so the first wire of a cluster cannot take the room
/// its siblings need (see <see cref="PinBreakoutPlanner"/>).
/// </summary>
public partial class WaveguideConnectionManager
{
    /// <summary>Plans the pin breakouts of this pass on the shared router.</summary>
    private void PlanPinBreakouts()
    {
        if (_router.PathfindingGrid == null) return;
        var wires = SnapshotConnections()
            .Where(IsBreakoutCandidate)
            .Select(c => new BreakoutWire(c.StartPin, c.EndPin,
                Math.Max(c.BendRadiusMicrometers, _router.ResolveProcessFloorFor(c.StartPin, c.EndPin))))
            .ToList();
        _router.PlanPinBreakouts(wires, WaveguideWidthMicrometers);
    }

    /// <summary>
    /// The re-routable wires on the other pins of the blocked wire's breakout clusters: the
    /// wire that took the room is usually one of them, and only re-routing them through
    /// their own breakouts gives every wire of the cluster its lane.
    /// </summary>
    private List<WaveguideConnection> BreakoutClusterSiblings(
        WaveguideConnection blocked, List<WaveguideConnection> all)
    {
        var clusterPins = new[] { blocked.StartPin, blocked.EndPin }
            .Where(p => p != null && _router.PinBreakoutPlans.ContainsKey(p))
            .SelectMany(p => _router.PinBreakoutPlans[p].Cluster.Pins)
            .ToHashSet();
        clusterPins.Remove(blocked.StartPin);
        clusterPins.Remove(blocked.EndPin);
        if (clusterPins.Count == 0) return new List<WaveguideConnection>();
        return all
            .Where(c => !ReferenceEquals(c, blocked) && IsReroutableSibling(c))
            .Where(c => clusterPins.Contains(c.StartPin) || clusterPins.Contains(c.EndPin))
            .ToList();
    }

    /// <summary>
    /// Wires whose geometry the router owns: auto, unfrozen, optical, not a chiplet facet
    /// link. Frozen and styled wires keep their geometry and take no part in breakouts.
    /// </summary>
    private static bool IsBreakoutCandidate(WaveguideConnection connection) =>
        connection.Type == WaveguideType.Auto
        && !connection.IsRouteFrozen
        && !connection.IsElectrical
        && !connection.IsCrossChipletFacetLink
        && connection.StartPin != null
        && connection.EndPin != null;
}
