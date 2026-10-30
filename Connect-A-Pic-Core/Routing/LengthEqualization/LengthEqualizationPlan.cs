using CAP_Core.Components.Connections;
using CAP_Core.Routing.MeanderGeneration;

namespace CAP_Core.Routing.LengthEqualization;

/// <summary>
/// One connection of an equalization: its meander request (poses, bounds, existing route,
/// process minimum) and the owners whose geometry it may touch — itself, connections sharing
/// one of its pins, and its own endpoint components.
/// </summary>
/// <param name="Connection">The connection to stretch.</param>
/// <param name="Request">Meander request at the plan's target; radius and obstacles are set while solving.</param>
/// <param name="IgnoredOwners">Indexed routes and footprints that are not obstacles for this connection.</param>
/// <param name="CurrentLengthMicrometers">Routed length when the plan was taken.</param>
public sealed record LengthEqualizationItem(
    WaveguideConnection Connection,
    MeanderRequest Request,
    IReadOnlySet<object> IgnoredOwners,
    double CurrentLengthMicrometers);

/// <summary>
/// Immutable snapshot of everything an equalization needs, taken on the thread that owns the
/// design so <see cref="LengthEqualizer.Solve"/> can run on a background thread.
/// </summary>
/// <param name="TargetLengthMicrometers">Length every item is stretched to.</param>
/// <param name="ToleranceMicrometers">Accepted deviation from the target.</param>
/// <param name="MaxRadiusMicrometers">Largest meander radius to try; smaller ones follow when it does not fit.</param>
/// <param name="MinSpacingMicrometers">Required centerline spacing to other routes.</param>
/// <param name="ContactClearanceMicrometers">Centerline distance below which two routes merge.</param>
/// <param name="Items">The connections to equalize.</param>
/// <param name="Routes">Every routed connection of the design (owner, route) — the obstacles.</param>
/// <param name="Footprints">Every routing-obstacle component (owner, rectangle).</param>
/// <param name="RequestedTargetMicrometers">The length the user asked for; null lets the solver pick the shortest common one.</param>
/// <param name="RerouteBundle">
/// Lay a bundle (pins facing each other, same order at both ends) out fresh instead of stretching
/// its existing routes.
/// </param>
public sealed record LengthEqualizationPlan(
    double TargetLengthMicrometers,
    double ToleranceMicrometers,
    double MaxRadiusMicrometers,
    double MinSpacingMicrometers,
    double ContactClearanceMicrometers,
    IReadOnlyList<LengthEqualizationItem> Items,
    IReadOnlyList<(object Owner, RoutedPath Path)> Routes,
    IReadOnlyList<(object Owner, MeanderBounds Footprint)> Footprints,
    double? RequestedTargetMicrometers = null,
    bool RerouteBundle = false);

/// <summary>What happened to one connection: its new route, or why none fit.</summary>
/// <param name="Connection">The connection.</param>
/// <param name="Path">The stretched route; null on failure.</param>
/// <param name="RadiusMicrometers">Meander radius the route uses (0 on failure).</param>
/// <param name="Failure">Why no route fit; null on success.</param>
/// <param name="BasePath">The route the plan started from, to detect edits made while solving.</param>
/// <param name="NotInBundle">
/// True when the selection was re-laid as a bundle and this connection is not part of it (it
/// loops back to its own column or runs another way); it is left unchanged.
/// </param>
public sealed record LengthEqualizationOutcome(
    WaveguideConnection Connection,
    RoutedPath? Path,
    double RadiusMicrometers,
    MeanderFailureReason? Failure,
    RoutedPath? BasePath,
    bool NotInBundle = false)
{
    /// <summary>True when a route at the target length was found.</summary>
    public bool IsSuccess => Path != null;
}

/// <summary>Result of an equalization run.</summary>
/// <param name="TargetLengthMicrometers">The common length.</param>
/// <param name="ToleranceMicrometers">Accepted deviation from it.</param>
/// <param name="Outcomes">One outcome per planned connection.</param>
/// <param name="Elapsed">Wall time of the solve.</param>
public sealed record LengthEqualizationResult(
    double TargetLengthMicrometers,
    double ToleranceMicrometers,
    IReadOnlyList<LengthEqualizationOutcome> Outcomes,
    TimeSpan Elapsed)
{
    /// <summary>Connections now at the target length.</summary>
    public int MatchedCount => Outcomes.Count(o => o.IsSuccess);

    /// <summary>Connections no route at the target fit for.</summary>
    public int FailedCount => Outcomes.Count(o => !o.IsSuccess && !o.NotInBundle);

    /// <summary>Selected connections left out because they are not part of the re-laid bundle.</summary>
    public int NotInBundleCount => Outcomes.Count(o => o.NotInBundle);

    /// <summary>True when the selection was re-laid as a bundle.</summary>
    public bool ReroutedAsBundle { get; init; }
}
