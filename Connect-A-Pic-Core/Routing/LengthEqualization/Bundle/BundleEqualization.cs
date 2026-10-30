using System.Diagnostics;

namespace CAP_Core.Routing.LengthEqualization.Bundle;

/// <summary>
/// The bundle path of an equalization: when the plan asks for it and the selection contains a
/// bundle, the bundle is re-laid at one common length; selected connections outside the bundle
/// are left as they are and reported.
/// </summary>
internal static class BundleEqualization
{
    /// <summary>The result of re-laying the plan's bundle, or null when it has none or it does not fit.</summary>
    /// <param name="plan">The snapshot to lay out.</param>
    /// <param name="radii">Radii to try, largest first, from the plan's maximum down to the wires' minimum.</param>
    /// <param name="cancellation">Stops the search.</param>
    /// <param name="equalize">One common length for every wire; false gives each wire its shortest shape.</param>
    public static LengthEqualizationResult? TrySolve(LengthEqualizationPlan plan, Func<double, double, IEnumerable<double>> radii,
        CancellationToken cancellation, bool equalize = true)
    {
        if (!plan.RerouteBundle)
            return null;
        var clock = Stopwatch.StartNew();
        var bundle = BundleDetector.Detect(plan.Items.Select(i => i.Request).ToList());
        if (bundle is not { } found)
            return null;
        var items = found.Wires.Select(w => w.Item).ToList();
        double minRadius = items.Max(i => plan.Items[i].Request.MinBendRadiusMicrometers);
        var solution = BundleRouter.TrySolve(plan, items, found.HeadingDegrees, found.Wires,
            radii(plan.MaxRadiusMicrometers, minRadius), equalize, cancellation);
        if (solution == null)
            return null;

        var routes = solution.Routes.ToDictionary(r => r.Item, r => r.Path);
        var outcomes = plan.Items.Select((item, i) => routes.TryGetValue(i, out var path)
                ? new LengthEqualizationOutcome(item.Connection, path, solution.Radius, null, item.Request.BasePath)
                : new LengthEqualizationOutcome(item.Connection, null, 0, null, item.Request.BasePath, NotInBundle: true))
            .ToList();
        return new LengthEqualizationResult(solution.TargetLength, plan.ToleranceMicrometers, outcomes, clock.Elapsed)
        {
            ReroutedAsBundle = true,
        };
    }
}
