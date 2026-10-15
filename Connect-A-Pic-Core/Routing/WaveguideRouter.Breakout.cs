using CAP_Core.Components.Core;
using CAP_Core.Routing.ImportedGeometry;
using CAP_Core.Routing.PinBreakout;

namespace CAP_Core.Routing;

/// <summary>
/// Breakout routing for tight pin clusters (<see cref="PinBreakoutPlanner"/>): a wire whose
/// pin has a breakout plan is routed between the plans' anchors on the grid, and the fixed
/// S-bends are stitched on at the cluster ends. The plans' reservations keep every other
/// wire out of the breakout region; a wire lifts its own pins' reservations while routing.
/// </summary>
public partial class WaveguideRouter
{
    private static readonly IReadOnlyDictionary<PhysicalPin, PinBreakoutPlan> NoBreakouts =
        new Dictionary<PhysicalPin, PinBreakoutPlan>();

    private IReadOnlyDictionary<PhysicalPin, PinBreakoutPlan> _pinBreakouts = NoBreakouts;
    private double _breakoutBandWidthMicrometers;

    /// <summary>
    /// Whether tight pin clusters are routed through planned breakouts. Disable to restore
    /// plain pin-to-pin routing for every wire.
    /// </summary>
    public bool UsePinBreakouts { get; set; } = true;

    /// <summary>The breakout plans of the current routing pass, keyed by pin.</summary>
    public IReadOnlyDictionary<PhysicalPin, PinBreakoutPlan> PinBreakoutPlans => _pinBreakouts;

    /// <summary>
    /// Plans breakouts for the tight clusters among <paramref name="wires"/> and reserves
    /// their regions on the grid, replacing the plans of an earlier pass.
    /// </summary>
    /// <param name="wires">The auto-routed optical wires of the design.</param>
    /// <param name="waveguideBandWidthMicrometers">Obstacle band width a routed wire occupies (µm).</param>
    public void PlanPinBreakouts(IEnumerable<BreakoutWire> wires, double waveguideBandWidthMicrometers)
    {
        ClearPinBreakouts();
        if (!UsePinBreakouts || PathfindingGrid == null) return;
        var planner = new PinBreakoutPlanner(PathfindingGrid, waveguideBandWidthMicrometers,
            MinWaveguideSpacingMicrometers, AllowedBendRadii);
        AdoptPinBreakouts(planner.Plan(wires), waveguideBandWidthMicrometers);
    }

    /// <summary>Drops every breakout plan and its grid reservation.</summary>
    public void ClearPinBreakouts()
    {
        _pinBreakouts = NoBreakouts;
        PathfindingGrid?.ClearBreakoutReservations();
    }

    /// <summary>Installs <paramref name="plans"/> and reserves their regions on this router's grid.</summary>
    internal void AdoptPinBreakouts(IReadOnlyDictionary<PhysicalPin, PinBreakoutPlan> plans, double bandWidthMicrometers)
    {
        _pinBreakouts = plans;
        _breakoutBandWidthMicrometers = bandWidthMicrometers;
        if (PathfindingGrid == null) return;
        foreach (var plan in plans.Values)
            PathfindingGrid.ReserveBreakout(plan.ReservationId, plan.ReservedSegments, bandWidthMicrometers);
    }

    /// <summary>The live plan of <paramref name="pin"/>: planned, and the pin has not moved since.</summary>
    private PinBreakoutPlan? ActiveBreakoutFor(PhysicalPin pin) =>
        _pinBreakouts.TryGetValue(pin, out var plan) && plan.MatchesCurrentPin() ? plan : null;

    /// <summary>
    /// Routes a wire with at least one planned pin. Order: the direct styled route exactly as
    /// without breakouts; then the breakout route between the anchors (own reservations
    /// lifted, the siblings' kept); then the plain route, again exactly as without breakouts.
    /// Breakouts therefore only ever add a way — a wire is never routed worse than before.
    /// </summary>
    private RoutedPath RouteWithBreakouts(PhysicalPin startPin, PhysicalPin endPin,
        PinBreakoutPlan? startPlan, PinBreakoutPlan? endPlan,
        double connectionRadius, double processFloor, CancellationToken cancellationToken)
    {
        double effectiveRadius = Math.Max(connectionRadius, processFloor);
        if (PreferDirectStyledRoutes)
        {
            var direct = WithClustersLifted(startPlan, endPlan, () => TryRouteDirect(startPin, endPin, effectiveRadius));
            if (direct != null)
                return direct;
        }

        var ownIds = new[] { startPlan, endPlan }.Where(p => p != null).Select(p => p!.ReservationId).ToList();
        var lifted = PathfindingGrid?.LiftBreakoutReservations(ownIds);
        try
        {
            var viaBreakout = TryRouteViaBreakout(startPin, endPin, startPlan, endPlan,
                connectionRadius, processFloor, cancellationToken);
            if (viaBreakout != null)
                return viaBreakout;
        }
        finally
        {
            if (lifted != null) PathfindingGrid?.RestoreCells(lifted);
        }

        return WithClustersLifted(startPlan, endPlan, () => RoutePinPair(startPin, endPin,
            connectionRadius, processFloor, tryDirect: false, cancellationToken));
    }

    /// <summary>
    /// Runs <paramref name="route"/> with the reservations of both clusters lifted — the grid
    /// the router sees without breakouts. Used for the attempts that must behave exactly as
    /// before breakouts existed.
    /// </summary>
    private T WithClustersLifted<T>(PinBreakoutPlan? startPlan, PinBreakoutPlan? endPlan, Func<T> route)
    {
        var clusterIds = new[] { startPlan, endPlan }
            .Where(p => p != null)
            .SelectMany(p => p!.Cluster.Pins)
            .Where(_pinBreakouts.ContainsKey)
            .Select(p => _pinBreakouts[p].ReservationId)
            .ToList();
        var lifted = PathfindingGrid?.LiftBreakoutReservations(clusterIds);
        try
        {
            return route();
        }
        finally
        {
            if (lifted != null) PathfindingGrid?.RestoreCells(lifted);
        }
    }

    /// <summary>
    /// Routes anchor to anchor (a pin without a plan is its own anchor) and stitches the
    /// planned S-bends on. Null when the middle route is blocked or the stitched route is
    /// not clean — the caller then routes the plain pin pair.
    /// </summary>
    private RoutedPath? TryRouteViaBreakout(PhysicalPin startPin, PhysicalPin endPin,
        PinBreakoutPlan? startPlan, PinBreakoutPlan? endPlan,
        double connectionRadius, double processFloor, CancellationToken cancellationToken)
    {
        if (BreakoutConflictsWithRoutedWires(startPlan, startPin, endPin)
            || BreakoutConflictsWithRoutedWires(endPlan, startPin, endPin))
            return null;

        var middleStart = startPlan?.CreateAnchorPin() ?? startPin;
        var middleEnd = endPlan?.CreateAnchorPin() ?? endPin;
        var middle = RoutePinPair(middleStart, middleEnd, connectionRadius, processFloor,
            tryDirect: PreferDirectStyledRoutes, cancellationToken);
        if (middle.IsBlockedFallback || middle.IsPlaceholderGeometry || !middle.IsValid)
        {
            ResetPlannedCrossings();
            return null;
        }

        var stitched = Stitch(middle, startPlan, endPlan);
        if (!stitched.IsValid || PathIntersectionDetector.HasSelfIntersection(stitched)
            || CrossesRegisteredWire(stitched, startPin, endPin))
        {
            ResetPlannedCrossings();
            return null;
        }
        return stitched;
    }

    /// <summary>Planned S-bend + middle route + reversed planned S-bend, as one route.</summary>
    private static RoutedPath Stitch(RoutedPath middle, PinBreakoutPlan? startPlan, PinBreakoutPlan? endPlan)
    {
        var stitched = new RoutedPath
        {
            ViolatesProcessMinBendRadius = middle.ViolatesProcessMinBendRadius,
            DebugGridPath = middle.DebugGridPath,
        };
        if (startPlan != null)
            stitched.Segments.AddRange(startPlan.BreakoutSegments);
        stitched.Segments.AddRange(middle.Segments);
        if (endPlan != null)
            stitched.Segments.AddRange(endPlan.BreakoutSegments.Reverse().Select(PathSegmentReversal.Reverse));
        return stitched;
    }

    /// <summary>
    /// True when a wire routed earlier (kept from a previous pass) already runs through the
    /// planned S-bend: the fixed geometry would cross or touch it.
    /// </summary>
    private bool BreakoutConflictsWithRoutedWires(PinBreakoutPlan? plan, PhysicalPin startPin, PhysicalPin endPin)
    {
        if (plan == null || PathfindingGrid == null) return false;
        var breakout = new RoutedPath();
        breakout.Segments.AddRange(plan.BreakoutSegments);
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        foreach (var segments in PathfindingGrid.GetWaveguideGeometries())
        {
            if (segments.Count == 0 || IsOwnStaleRoute(segments, startX, startY, endX, endY))
                continue;
            var other = new RoutedPath();
            other.Segments.AddRange(segments);
            if (PathIntersectionDetector.Crosses(breakout, other)
                || PathIntersectionDetector.ComesCloserThan(breakout, other, SiblingContactClearanceMicrometers,
                    endpointExemptionRadiusMicrometers: 0))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the stitched route properly crosses a registered wire other than through a
    /// planned crossing. The grid search near the anchors works with opened-up corridors, so the
    /// exact geometry decides — a breakout route must never look clean while cutting a wire.
    /// </summary>
    private bool CrossesRegisteredWire(RoutedPath route, PhysicalPin startPin, PhysicalPin endPin)
    {
        if (PathfindingGrid == null) return false;
        var bounds = route.Segments.Select(s => PathSegmentBounds.Of(s)).ToList();
        var crossedByPlan = LastPlannedCrossings.Select(c => c.CrossedConnection).ToHashSet();
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        foreach (var (owner, other) in PathfindingGrid.WaveguidesNear(
                     bounds.Min(b => b.MinX), bounds.Min(b => b.MinY), bounds.Max(b => b.MaxX), bounds.Max(b => b.MaxY)))
        {
            if (crossedByPlan.Contains(owner) || IsOwnStaleRoute(other.Segments, startX, startY, endX, endY))
                continue;
            if (PathIntersectionDetector.Crosses(route, other)) return true;
        }
        return false;
    }

    /// <summary>True when any cell under <paramref name="segments"/> is held by a breakout reservation.</summary>
    private bool RunsThroughBreakoutReservation(IEnumerable<PathSegment> segments)
    {
        if (PathfindingGrid == null || _pinBreakouts.Count == 0) return false;
        return IsPathBlocked(segments, PathfindingGrid.IsBreakoutReservedCell);
    }
}
