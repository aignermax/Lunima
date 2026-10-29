using System.Diagnostics;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.LengthEqualization.Bundle;
using CAP_Core.Routing.MeanderGeneration;
using CAP_Core.Routing.MeanderGeneration.Clearance;

namespace CAP_Core.Routing.LengthEqualization;

/// <summary>
/// Gives a set of connections one common length. A bundle — pins facing each other across a
/// channel, wires in the same order at both ends — is re-laid from scratch as nested arches at
/// the shortest common length it allows (see <see cref="BundleRouter"/>). Otherwise, or when the
/// bundle does not fit, the common length is by default the longest route, so none has to be
/// shortened: each connection keeps its existing route and gets a meander folded in
/// wherever there is room — the largest radius first, smaller ones only where the large one does
/// not fit. Connections are processed most-missing-length first against one shared
/// <see cref="ClearanceIndex"/> that is updated after every placement, so later meanders keep
/// clear of earlier ones and nobody overlaps. <see cref="Prepare"/> snapshots the live design;
/// <see cref="Solve"/> is pure and may run off the UI thread.
/// </summary>
public sealed class LengthEqualizer
{
    /// <summary>Each fallback radius is this fraction of the previous one.</summary>
    private const double RadiusStepFactor = 0.7;
    private const int MaxRadiusSteps = 4;
    private const double PinOnFootprintToleranceMicrometers = 1e-3;
    /// <summary>Routes this short join abutting pins; there is nothing to stretch.</summary>
    private const double AbuttingLengthMicrometers = 1e-3;
    /// <summary>Widths equal to this many decimals (µm) count as the same.</summary>
    private const int WidthDecimals = 3;

    private readonly ConnectionLengthMatcher _matcher;
    private readonly WaveguideRouter? _router;

    /// <summary>Creates an equalizer; the router supplies spacing and the per-connection process floor.</summary>
    public LengthEqualizer(WaveguideRouter? router = null)
    {
        _router = router;
        _matcher = new ConnectionLengthMatcher(router);
    }

    /// <summary>
    /// Snapshots the design for an equalization of <paramref name="connections"/>.
    /// </summary>
    /// <param name="connections">The connections to give one length; ones without a usable route are skipped.</param>
    /// <param name="allConnections">Every connection of the design (obstacles).</param>
    /// <param name="components">Every component of the design (obstacles).</param>
    /// <param name="targetLengthMicrometers">Common length; null uses the longest of <paramref name="connections"/>.</param>
    /// <param name="maxRadiusMicrometers">Largest meander radius to try.</param>
    /// <param name="toleranceMicrometers">Accepted deviation from the target.</param>
    /// <param name="rerouteBundle">Re-lay a bundle from scratch instead of stretching its existing routes.</param>
    public LengthEqualizationPlan Prepare(
        IReadOnlyList<WaveguideConnection> connections,
        IReadOnlyList<WaveguideConnection> allConnections,
        IReadOnlyList<Component> components,
        double? targetLengthMicrometers,
        double maxRadiusMicrometers,
        double toleranceMicrometers,
        bool rerouteBundle = false)
    {
        var usable = connections.Distinct().Where(c => IsUsable(c) || (rerouteBundle && HasPins(c))).ToList();
        double target = targetLengthMicrometers ?? (usable.Count == 0 ? 0 : usable.Max(c => c.PathLengthMicrometers));
        var obstacleComponents = components.Where(c => c.IsRoutingObstacle).ToList();
        var items = usable.Select(c => new LengthEqualizationItem(
                c,
                Request(c, components, target, toleranceMicrometers, maxRadiusMicrometers),
                IgnoredOwners(c, allConnections, obstacleComponents),
                c.PathLengthMicrometers))
            .ToList();
        var routes = allConnections.Where(IsUsable).Select(c => ((object)c, c.RoutedPath!)).ToList();
        var footprints = obstacleComponents.Select(c => ((object)c, Footprint(c))).ToList();
        double spacing = _router?.MinWaveguideSpacingMicrometers ?? PhotonicConstants.DefaultMinWaveguideSpacingMicrometers;
        return new LengthEqualizationPlan(target, toleranceMicrometers, maxRadiusMicrometers, spacing,
            PhotonicConstants.StandardWaveguideWidthMicrometers, items, routes, footprints,
            targetLengthMicrometers, rerouteBundle);
    }

    /// <summary>
    /// The connection's meander request, keeping its existing route first. Without a process
    /// floor the connection's own bend radius is the smallest radius the fallback may use.
    /// </summary>
    private MeanderRequest Request(WaveguideConnection connection, IReadOnlyList<Component> components,
        double target, double tolerance, double maxRadius)
    {
        var request = _matcher.BuildRequest(connection, components, target, tolerance,
            Array.Empty<WaveguideConnection>(), maxRadius);
        double floor = _matcher.ResolveProcessFloor(connection);
        double minRadius = floor > 0 ? floor : Math.Min(connection.BendRadiusMicrometers, maxRadius);
        return request with { MinBendRadiusMicrometers = minRadius, PreferExistingRoute = true };
    }

    /// <summary>
    /// Finds a route at one common length for every item it can; pure, so it may run on a
    /// background thread. A bundle is re-laid when the plan asks for it; otherwise items are
    /// stretched most-missing-length first.
    /// </summary>
    public static LengthEqualizationResult Solve(LengthEqualizationPlan plan, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var bundle = BundleEqualization.TrySolve(plan, Radii, cancellation);
        var stretch = Stretch(plan, cancellation);
        return bundle == null || Prefers(stretch, bundle) ? stretch : bundle;
    }

    /// <summary>
    /// Meanders on the existing routes win over a re-laid bundle only when they match every
    /// connection at a clearly shorter common length — e.g. when the existing routes run diagonally
    /// and the bundle's axis-parallel arches would come out longer.
    /// </summary>
    private static bool Prefers(LengthEqualizationResult stretch, LengthEqualizationResult bundle) =>
        stretch.FailedCount == 0 && stretch.MatchedCount >= bundle.MatchedCount
        && stretch.TargetLengthMicrometers < bundle.TargetLengthMicrometers - stretch.ToleranceMicrometers;

    /// <summary>
    /// Lays the bundle among the plan's connections out as planar arches, each wire at its
    /// shortest shape (or at the plan's requested common length) — the fast, crossing-free route
    /// for facing pin columns that the general router would search for one wire at a time. Needs
    /// no existing routes. Connections outside the bundle come back with
    /// <see cref="LengthEqualizationOutcome.NotInBundle"/>; null when there is no bundle or it does
    /// not fit cleanly.
    /// </summary>
    public static LengthEqualizationResult? LayOutBundle(LengthEqualizationPlan plan, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return BundleEqualization.TrySolve(plan, Radii, cancellation, equalize: plan.RequestedTargetMicrometers != null);
    }

    private static LengthEqualizationResult Stretch(LengthEqualizationPlan plan, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var index = new ClearanceIndex();
        foreach (var (owner, path) in plan.Routes)
            index.SetRoute(owner, path);
        foreach (var (owner, footprint) in plan.Footprints)
            index.AddFootprint(footprint, owner);

        var generator = new MeanderPathGenerator();
        var outcomes = new List<LengthEqualizationOutcome>();
        foreach (var item in plan.Items.OrderByDescending(i => plan.TargetLengthMicrometers - i.CurrentLengthMicrometers))
        {
            cancellation.ThrowIfCancellationRequested();
            var outcome = SolveItem(item, plan, index, generator);
            if (outcome.Path != null)
                index.SetRoute(item.Connection, outcome.Path);
            outcomes.Add(outcome);
        }
        return new LengthEqualizationResult(plan.TargetLengthMicrometers, plan.ToleranceMicrometers, outcomes, clock.Elapsed);
    }

    private static LengthEqualizationOutcome SolveItem(LengthEqualizationItem item, LengthEqualizationPlan plan,
        ClearanceIndex index, MeanderPathGenerator generator)
    {
        var obstacles = MeanderObstacles.FromIndex(index, item.IgnoredOwners,
            plan.MinSpacingMicrometers, plan.ContactClearanceMicrometers);
        MeanderFailureReason? failure = null;
        foreach (double radius in Radii(plan.MaxRadiusMicrometers, item.Request.MinBendRadiusMicrometers))
        {
            var result = generator.Generate(item.Request with { MeanderRadiusMicrometers = radius, Obstacles = obstacles });
            if (result.IsSuccess)
                return new LengthEqualizationOutcome(item.Connection, result.Path, radius, null, item.Request.BasePath);
            failure = result.FailureReason;
            if (failure is MeanderFailureReason.TargetShorterThanDirectPath)
                break;
        }
        return new LengthEqualizationOutcome(item.Connection, null, 0, failure, item.Request.BasePath);
    }

    /// <summary>The largest radius first, then progressively smaller ones down to the process minimum.</summary>
    internal static IEnumerable<double> Radii(double maxRadius, double minRadius)
    {
        double radius = Math.Max(maxRadius, minRadius);
        for (int step = 0; step < MaxRadiusSteps && radius > minRadius; step++, radius *= RadiusStepFactor)
            yield return radius;
        yield return minRadius;
    }

    /// <summary>
    /// The width the equalized routes should share: the most common one among
    /// <paramref name="connections"/> (the wider on a tie). Equal lengths only give equal phase
    /// when the cross-sections match, so mixed widths are unified.
    /// </summary>
    public static double CommonWidth(IEnumerable<WaveguideConnection> connections) =>
        connections.GroupBy(c => Math.Round(c.WidthMicrometers, WidthDecimals))
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
            .Select(g => g.First().WidthMicrometers)
            .DefaultIfEmpty(PhotonicConstants.StandardWaveguideWidthMicrometers)
            .First();

    private static bool HasPins(WaveguideConnection connection) =>
        connection.StartPin != null && connection.EndPin != null;

    private static bool IsUsable(WaveguideConnection connection) =>
        connection.StartPin != null && connection.EndPin != null
        && connection.RoutedPath is { Segments.Count: > 0 } && !connection.IsBlockedFallback
        && connection.PathLengthMicrometers > AbuttingLengthMicrometers;

    private static IReadOnlySet<object> IgnoredOwners(WaveguideConnection connection,
        IReadOnlyList<WaveguideConnection> allConnections, IReadOnlyList<Component> obstacleComponents)
    {
        var ignored = new HashSet<object>(ReferenceEqualityComparer.Instance) { connection };
        foreach (var other in allConnections.Where(other => SharesPin(connection, other)))
            ignored.Add(other);
        foreach (var component in obstacleComponents.Where(c => HoldsPin(c, connection.StartPin) || HoldsPin(c, connection.EndPin)))
            ignored.Add(component);
        return ignored;
    }

    private static bool SharesPin(WaveguideConnection a, WaveguideConnection b) =>
        ReferenceEquals(a.StartPin, b.StartPin) || ReferenceEquals(a.StartPin, b.EndPin)
        || ReferenceEquals(a.EndPin, b.StartPin) || ReferenceEquals(a.EndPin, b.EndPin);

    private static bool HoldsPin(Component component, PhysicalPin? pin)
    {
        if (pin == null)
            return false;
        var (x, y) = pin.GetAbsolutePosition();
        var footprint = Footprint(component);
        return x >= footprint.MinX - PinOnFootprintToleranceMicrometers && x <= footprint.MaxX + PinOnFootprintToleranceMicrometers
            && y >= footprint.MinY - PinOnFootprintToleranceMicrometers && y <= footprint.MaxY + PinOnFootprintToleranceMicrometers;
    }

    private static MeanderBounds Footprint(Component component) => new(
        component.PhysicalX, component.PhysicalY,
        component.PhysicalX + component.WidthMicrometers, component.PhysicalY + component.HeightMicrometers);
}
