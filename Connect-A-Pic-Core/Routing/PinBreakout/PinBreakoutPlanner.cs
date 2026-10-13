using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Routing.PinBreakout;

/// <summary>
/// Plans breakouts for tight pin clusters. Wires to pins closer together than
/// <see cref="RoutablePitchMicrometers"/> cannot all reach their pins on the grid: the final
/// bends of neighbouring wires need more room than the pin pitch leaves. Each wire of a
/// cluster gets fixed, exact geometry out of the face (<see cref="BreakoutFan"/>) ending at
/// an anchor where the grid route takes over. Wires whose far end lies to one side turn
/// towards it in staggered parallel lanes; the others leave straight, spread to the
/// routable pitch by S-bends. Beyond each anchor a short straight lead is reserved so a
/// sibling cannot cut across the anchor before its own wire arrives.
/// </summary>
public sealed class PinBreakoutPlanner
{
    /// <summary>Fraction of the pin pitch two planned breakouts must keep apart (numerical slack).</summary>
    private const double PitchKeepFraction = 0.99;

    private readonly PathfindingGrid _grid;

    /// <summary>Creates a planner for the given grid and waveguide obstacle band width (µm).</summary>
    public PinBreakoutPlanner(PathfindingGrid grid, double waveguideBandWidthMicrometers,
        double minWaveguideSpacingMicrometers, IReadOnlyList<double> allowedBendRadii)
    {
        _grid = grid;
        AllowedBendRadii = allowedBendRadii;
        RoutablePitchMicrometers = ComputeRoutablePitch(
            grid.CellSizeMicrometers, waveguideBandWidthMicrometers, minWaveguideSpacingMicrometers);
    }

    /// <summary>Foundry radii the breakout arcs snap up to (empty: any radius).</summary>
    public IReadOnlyList<double> AllowedBendRadii { get; }

    /// <summary>
    /// Smallest pin pitch (µm) at which two wires can run parallel into neighbouring pins on
    /// the grid: one wire's obstacle band plus the minimum gap, rounded up to whole cells,
    /// plus one free cell for the neighbour's own grid centreline.
    /// </summary>
    public double RoutablePitchMicrometers { get; }

    /// <summary>See <see cref="RoutablePitchMicrometers"/>.</summary>
    public static double ComputeRoutablePitch(double cellSize, double bandWidth, double minSpacing) =>
        (Math.Ceiling((bandWidth + minSpacing) / cellSize) + 1) * cellSize;

    /// <summary>
    /// Plans every tight cluster among the wires' pins. Clusters whose breakout region runs
    /// into a foreign component body or a frozen path get no plan (their wires route as before).
    /// </summary>
    public Dictionary<PhysicalPin, PinBreakoutPlan> Plan(IEnumerable<BreakoutWire> wires)
    {
        var radiusByPin = new Dictionary<PhysicalPin, double>();
        var farEndByPin = new Dictionary<PhysicalPin, (double X, double Y)>();
        foreach (var wire in wires)
        {
            Register(radiusByPin, farEndByPin, wire.Start, wire.End, wire.BendRadiusMicrometers);
            Register(radiusByPin, farEndByPin, wire.End, wire.Start, wire.BendRadiusMicrometers);
        }

        var plans = new Dictionary<PhysicalPin, PinBreakoutPlan>();
        foreach (var cluster in PinClusterDetector.Detect(radiusByPin.Keys, RoutablePitchMicrometers))
        {
            double radius = SnapRadius(cluster.Pins.Max(p => radiusByPin[p]));
            var sides = BreakoutSideSelector.MakeMonotone(
                cluster.Pins.Select(p => WishedSide(p, farEndByPin[p], cluster.HeadingDegrees)).ToList());
            var clusterPlans = PlanCluster(cluster, radius, sides)
                ?? PlanCluster(cluster, radius, new int[cluster.Pins.Count]);
            if (clusterPlans == null) continue;
            foreach (var plan in clusterPlans)
                plans[plan.Pin] = plan;
        }
        return plans;
    }

    /// <summary>
    /// Plans one cluster with arc radius <paramref name="radius"/> and per-pin
    /// <paramref name="sides"/> (monotone, see <see cref="BreakoutSideSelector"/>), or null
    /// when the breakouts are obstructed or would come closer than the pin pitch.
    /// </summary>
    public List<PinBreakoutPlan>? PlanCluster(PinCluster cluster, double radius, IReadOnlyList<int> sides)
    {
        // The escape run clears the components' clearance band at grid resolution, like the
        // straight run every grid route takes out of a pin.
        double escape = _grid.ObstaclePaddingMicrometers + _grid.CellSizeMicrometers;
        var fan = new BreakoutFan(cluster, radius, RoutablePitchMicrometers, _grid.CellSizeMicrometers, escape);
        var own = cluster.Pins.Select(p => p.ParentComponent).ToHashSet();
        double step = _grid.CellSizeMicrometers / 2;
        for (double extraRun = 0; extraRun <= RoutablePitchMicrometers; extraRun += step)
        {
            var plans = TryPlanFan(cluster, fan, sides, extraRun, radius, own);
            if (plans != null) return plans;
        }
        return null;
    }

    /// <summary>
    /// The fan with every arc moved <paramref name="extraRun"/> out of the face, or null when
    /// a breakout or its full lead touches a foreign component (body or clearance band) or a
    /// frozen path, or neighbouring breakouts come closer than their pin pitch.
    /// </summary>
    private List<PinBreakoutPlan>? TryPlanFan(PinCluster cluster, BreakoutFan fan, IReadOnlyList<int> sides,
        double extraRun, double radius, IReadOnlySet<Component> own)
    {
        double leadLength = radius + _grid.CellSizeMicrometers;
        var plans = new List<PinBreakoutPlan>();
        for (int k = 0; k < cluster.Pins.Count; k++)
        {
            var breakout = fan.Build(k, sides, extraRun);
            if (breakout.Count == 0 || IsObstructed(breakout, own))
                return null;
            var end = breakout[^1];
            var lead = BuildLead(end.EndPoint, end.EndAngleDegrees, leadLength);
            if (IsObstructed(lead, own))
                return null;
            plans.Add(new PinBreakoutPlan(cluster.Pins[k], breakout, lead, radius, cluster));
        }
        return KeepsPitch(plans, fan) ? plans : null;
    }

    /// <summary>True when neighbouring breakouts keep at least the pin pitch between them.</summary>
    private static bool KeepsPitch(List<PinBreakoutPlan> plans, BreakoutFan fan)
    {
        for (int k = 1; k < plans.Count; k++)
        {
            var a = new RoutedPath();
            a.Segments.AddRange(plans[k - 1].BreakoutSegments);
            var b = new RoutedPath();
            b.Segments.AddRange(plans[k].BreakoutSegments);
            if (PathIntersectionDetector.MinimumDistance(a, b) < fan.PitchBefore(k) * PitchKeepFraction)
                return false;
        }
        return true;
    }

    /// <summary>The straight lead of <paramref name="length"/> µm beyond the anchor.</summary>
    private static List<PathSegment> BuildLead((double X, double Y) anchor, double headingDegrees, double length)
    {
        double rad = headingDegrees * Math.PI / 180.0;
        return new List<PathSegment>
        {
            new StraightSegment(anchor.X, anchor.Y,
                anchor.X + length * Math.Cos(rad), anchor.Y + length * Math.Sin(rad), headingDegrees),
        };
    }

    private bool IsObstructed(IEnumerable<PathSegment> segments, IReadOnlySet<Component> own)
    {
        double step = _grid.CellSizeMicrometers / 2;
        foreach (var (x, y) in SegmentSampling.Points(segments, step))
        {
            var (gx, gy) = _grid.PhysicalToGrid(x, y);
            if (_grid.ObstructsBreakout(gx, gy, own)) return true;
        }
        return false;
    }

    /// <summary>The smallest allowed radius at or above <paramref name="radius"/> (the radius itself when none is).</summary>
    private double SnapRadius(double radius)
    {
        foreach (var allowed in AllowedBendRadii.OrderBy(r => r))
        {
            if (allowed >= radius) return allowed;
        }
        return radius;
    }

    private static int WishedSide(PhysicalPin pin, (double X, double Y) farEnd, double headingDegrees)
    {
        var (x, y) = pin.GetAbsolutePosition();
        double forward = PinClusterDetector.Forward(farEnd.X - x, farEnd.Y - y, headingDegrees);
        double lateral = PinClusterDetector.Lateral(farEnd.X - x, farEnd.Y - y, headingDegrees);
        return BreakoutSideSelector.PreferredSide(forward, lateral);
    }

    /// <summary>
    /// Records a wire end: the largest radius the pin's wires need and the far end of its
    /// first wire. Crossing ports are left out: a crossing chain places its cells with its own
    /// docking geometry, so their pins never break out.
    /// </summary>
    private static void Register(Dictionary<PhysicalPin, double> radiusByPin,
        Dictionary<PhysicalPin, (double X, double Y)> farEndByPin, PhysicalPin pin, PhysicalPin farPin, double radius)
    {
        if (CrossingComponentCatalog.IsCrossing(pin.ParentComponent)) return;
        radiusByPin[pin] = Math.Max(radiusByPin.GetValueOrDefault(pin), radius);
        if (!farEndByPin.ContainsKey(pin))
            farEndByPin[pin] = farPin.GetAbsolutePosition();
    }
}
