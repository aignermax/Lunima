using CAP_Core.Routing.MeanderGeneration;
using CAP_Core.Routing.MeanderGeneration.Clearance;

namespace CAP_Core.Routing.LengthEqualization.Bundle;

/// <summary>
/// Re-lays a bundle from scratch instead of stretching its existing routes: pins facing each
/// other across a channel, wires in the same order at both ends. Every wire gets a fresh route
/// of nested arches at the shortest common length the bundle allows (or the requested one), with
/// the largest radius that fits at a comfortable pitch (the minimum pitch only when nothing else
/// fits) — so a bundle whose old routes were tangled or needlessly long
/// still comes out equal. Each new route is checked against everything else in the design; when
/// no radius, pitch and pin clearance gives a clean bundle, the caller falls back to meanders.
/// </summary>
internal static class BundleRouter
{
    /// <summary>Centre-line pitch the arches are laid at when there is room.</summary>
    private const double PreferredPitchMicrometers = 10.0;
    private const double PinExemptionRadii = 3.0;
    private const double EndpointToleranceMicrometers = 1e-3;
    private static readonly double[] LeadRadii = { 0.05, 1, 2, 4, 8 };

    /// <summary>A routed bundle: the common length and one route per wire.</summary>
    public sealed record Solution(double TargetLength, double Radius, IReadOnlyList<(int Item, RoutedPath Path)> Routes);

    /// <summary>The bundle laid out cleanly, or null when it cannot be.</summary>
    public static Solution? TrySolve(LengthEqualizationPlan plan, IReadOnlyList<int> items, double heading,
        IReadOnlyList<BundleWire> wires, IEnumerable<double> radii, CancellationToken cancellation)
    {
        var index = ObstacleIndex(plan, items);
        double minPitch = plan.MinSpacingMicrometers + items.Max(i => plan.Items[i].Connection.WidthMicrometers);
        var pitches = new[] { Math.Max(PreferredPitchMicrometers, minPitch), minPitch }.Distinct().ToList();
        var radiusList = radii.ToList();
        foreach (double pitch in pitches)
        {
            foreach (double radius in radiusList)
            {
                foreach (double lead in LeadRadii.Select(f => f * radius))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var layout = BundleLayout.Build(wires, radius, pitch, lead, plan.RequestedTargetMicrometers);
                    if (layout == null)
                        continue;
                    var routes = Emit(plan, heading, layout);
                    if (routes != null && Clear(plan, index, routes, radius))
                        return new Solution(layout.TargetLength, radius, routes);
                }
            }
        }
        return null;
    }

    private static ClearanceIndex ObstacleIndex(LengthEqualizationPlan plan, IReadOnlyList<int> items)
    {
        var rerouted = new HashSet<object>(items.Select(i => (object)plan.Items[i].Connection), ReferenceEqualityComparer.Instance);
        var index = new ClearanceIndex();
        foreach (var (owner, path) in plan.Routes.Where(r => !rerouted.Contains(r.Owner)))
            index.SetRoute(owner, path);
        foreach (var (owner, footprint) in plan.Footprints)
            index.AddFootprint(footprint, owner);
        return index;
    }

    private static List<(int Item, RoutedPath Path)>? Emit(LengthEqualizationPlan plan, double heading, BundleLayout.Plan layout)
    {
        var routes = new List<(int, RoutedPath)>();
        foreach (var shape in layout.Shapes)
        {
            var request = plan.Items[shape.Wire.Item].Request;
            var path = BundlePathEmitter.Emit(shape, request, heading);
            var end = path.Segments[^1].EndPoint;
            if (Math.Abs(end.X - request.EndX) > EndpointToleranceMicrometers || Math.Abs(end.Y - request.EndY) > EndpointToleranceMicrometers)
                return null;
            routes.Add((shape.Wire.Item, path));
        }
        return routes;
    }

    /// <summary>
    /// True when every new route keeps clear of the rest of the design and of the routes laid
    /// before it. The index is left as it was either way.
    /// </summary>
    private static bool Clear(LengthEqualizationPlan plan, ClearanceIndex index, List<(int Item, RoutedPath Path)> routes, double radius)
    {
        var placed = new List<object>();
        try
        {
            foreach (var (item, path) in routes)
            {
                var entry = plan.Items[item];
                var obstacles = MeanderObstacles.FromIndex(index, entry.IgnoredOwners,
                    plan.MinSpacingMicrometers, plan.ContactClearanceMicrometers);
                if (MeanderCollisionChecker.Collides(path, obstacles, PinExemptionRadii * radius))
                    return false;
                index.SetRoute(entry.Connection, path);
                placed.Add(entry.Connection);
            }
            return true;
        }
        finally
        {
            foreach (var owner in placed)
                index.RemoveRoute(owner);
        }
    }
}
