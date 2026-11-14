using CAP_Core.Components.Connections;
using CAP_Core.Components.PinKinds;
using CAP_Core.Routing;
using CAP_Core.Routing.LengthEqualization;
using CAP_Core.Routing.Snapshots;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Switching several connections back to Auto: when they form a bundle of facing pins, all of
/// them are laid out at once as planar arches — instead of the general router searching for one
/// wire at a time, which on a dense chip takes minutes and can fail on pin pairs a few µm apart.
/// Connections that share one length target keep it: the bundle is laid out at that length — as
/// arches, or, when the arches would be too long, routed and meandered to it like "Equalize
/// lengths" does. A lock no layout meets is kept and reported, never silently dropped.
/// </summary>
internal static class BundleAutoRouting
{
    /// <summary>Fewer connections than this are left to the general router.</summary>
    public const int MinimumConnections = 2;

    /// <summary>Largest arch or meander radius tried; smaller ones follow where it does not fit.</summary>
    private const double MaxRadiusMicrometers = 250.0;
    /// <summary>Smallest meander radius on routed paths — the one "Equalize lengths" suggests: tighter loops lose light.</summary>
    private const double MinMeanderRadiusMicrometers = 25.0;
    private const double ToleranceMicrometers = 0.1;
    private const double SameTargetMicrometers = 1e-3;
    private const int TargetDecimals = 3;

    /// <summary>
    /// The laid-out route of every connection in the bundle (the others are left to the general
    /// router) plus, when the bundle's shared length lock could not be kept, why; null when there
    /// is no bundle or it does not fit cleanly. The design is read on the calling thread; the
    /// search, including the fallback for a lock the arches cannot meet, runs on a background thread.
    /// </summary>
    public static async Task<BundleAutoLayout?> TryLayOutAsync(
        DesignCanvasViewModel canvas, IReadOnlyList<WaveguideConnectionViewModel> targets)
    {
        var connections = targets.Select(t => t.Connection).Where(IsOptical).Distinct().ToList();
        if (connections.Count < MinimumConnections)
            return null;
        double tolerance = Tolerance(connections);
        var shortest = Prepare(canvas, connections, null, tolerance);
        double? common = MostCommonTarget(connections);
        var locked = common is { } target
            ? new LockedPlan(target, tolerance, Prepare(canvas, connections, target, tolerance), Capture(canvas))
            : null;
        return await Task.Run(() => Solve(shortest, locked));
    }

    /// <summary>
    /// Shortest shapes, unless every bundle wire shares one length target: then the bundle is laid
    /// out at that length so the constraint still holds.
    /// </summary>
    private static BundleAutoLayout? Solve(LengthEqualizationPlan shortest, LockedPlan? locked)
    {
        var result = LengthEqualizer.LayOutBundle(shortest);
        if (result == null)
            return null;
        var bundle = result.Outcomes.Where(o => o.IsSuccess).ToList();
        if (locked == null || !bundle.All(o => SharesTarget(o.Connection, locked.Target)))
            return Layout(result);
        return LengthEqualizer.LayOutBundle(locked.AtTarget) is { } arches
            ? Layout(arches)
            : KeepLock(locked, bundle);
    }

    /// <summary>
    /// The arches cannot meet the lock: route the bundle and meander it to the lock instead. When
    /// that misses routes too, the miss is reported and the routes keep as many on the lock as
    /// possible — the meandered ones plus the router's routes for the rest, or, when no meander
    /// fits at all, the shortest arches.
    /// </summary>
    private static BundleAutoLayout KeepLock(LockedPlan locked, IReadOnlyList<LengthEqualizationOutcome> arches)
    {
        var byId = arches.ToDictionary(o => o.Connection.Id, o => o.Connection);
        var rerouted = LengthLockRerouter.Solve(locked.Design, byId.Keys, locked.Target, locked.Tolerance,
            MaxRadiusMicrometers, MinMeanderRadiusMicrometers);
        if (rerouted.AllMatched)
            return new BundleAutoLayout(ById(rerouted.Matched, byId));

        double archMinimum = arches.Max(o => o.Path!.TotalLengthMicrometers);
        var miss = new BundleLockMiss(locked.Target, archMinimum, arches.Count - rerouted.Matched.Count, arches.Count);
        if (rerouted.Matched.Count == 0)
            return new BundleAutoLayout(arches.ToDictionary(o => o.Connection, o => o.Path!), miss);
        var routes = rerouted.Matched.Concat(rerouted.Missed).ToDictionary(p => p.Key, p => p.Value);
        return new BundleAutoLayout(ById(routes, byId), miss);
    }

    private static BundleAutoLayout Layout(LengthEqualizationResult result) =>
        new(result.Outcomes.Where(o => o.IsSuccess).ToDictionary(o => o.Connection, o => o.Path!));

    private static Dictionary<WaveguideConnection, RoutedPath> ById(IReadOnlyDictionary<Guid, RoutedPath> routes,
        IReadOnlyDictionary<Guid, WaveguideConnection> connections) =>
        routes.Where(p => connections.ContainsKey(p.Key)).ToDictionary(p => connections[p.Key], p => p.Value);

    private static bool SharesTarget(WaveguideConnection connection, double target) =>
        connection.TargetLengthMicrometers is { } t && Math.Abs(t - target) <= SameTargetMicrometers;

    private static LengthEqualizationPlan Prepare(DesignCanvasViewModel canvas, List<WaveguideConnection> connections,
        double? target, double tolerance) =>
        new LengthEqualizer(canvas.Router).Prepare(connections, canvas.ConnectionManager.Connections.ToList(),
            canvas.Components.Select(c => c.Component).ToList(), target, MaxRadiusMicrometers, tolerance,
            rerouteBundle: true);

    /// <summary>The design as the routing pass captures it, so the fallback routes exactly like the pass would.</summary>
    private static RoutingInputSnapshot Capture(DesignCanvasViewModel canvas) =>
        RoutingInputSnapshot.Capture(canvas.Components.Select(c => c.Component).ToArray(), canvas.Router,
            canvas.ConnectionManager, preserveCachedGrid: false);

    /// <summary>The tightest recorded lock tolerance, so a laid-out route meets every lock it carries.</summary>
    private static double Tolerance(IReadOnlyList<WaveguideConnection> connections) =>
        connections.Select(c => c.LengthToleranceMicrometers ?? ToleranceMicrometers)
            .Where(t => t > 0).DefaultIfEmpty(ToleranceMicrometers).Min();

    /// <summary>The length target most of the connections carry, or null when none has one.</summary>
    private static double? MostCommonTarget(IReadOnlyList<WaveguideConnection> connections) =>
        connections.Where(c => c.TargetLengthMicrometers != null)
            .GroupBy(c => Math.Round(c.TargetLengthMicrometers!.Value, TargetDecimals))
            .OrderByDescending(g => g.Count())
            .Select(g => g.First().TargetLengthMicrometers)
            .FirstOrDefault();

    private static bool IsOptical(WaveguideConnection connection) =>
        !PinKindHelper.IsElectrical(connection.StartPin) || !PinKindHelper.IsElectrical(connection.EndPin);

    /// <summary>Everything needed to lay a locked bundle out at its length, captured on the UI thread.</summary>
    private sealed record LockedPlan(double Target, double Tolerance, LengthEqualizationPlan AtTarget, RoutingInputSnapshot Design);
}
