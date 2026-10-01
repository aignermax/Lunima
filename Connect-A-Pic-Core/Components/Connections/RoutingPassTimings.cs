namespace CAP_Core.Components.Connections;

/// <summary>
/// Immutable per-pass wall-clock breakdown of one full re-route
/// (<see cref="WaveguideConnectionManager.RecalculateAllTransmissions"/>). Every pass of the
/// pipeline reports its own duration so router profiling can show where the time goes instead
/// of guessing. Passes skipped by an early return or cancellation report <see cref="TimeSpan.Zero"/>;
/// what did run is always reported (<see cref="WasCancelled"/> marks a cut-short pass).
/// </summary>
public sealed record RoutingPassTimings
{
    /// <summary>Wall-clock of the whole re-route, including uninstrumented glue between passes.</summary>
    public TimeSpan Total { get; init; }

    /// <summary>Dissolution of stale crossing records before routing starts.</summary>
    public TimeSpan CrossingDissolution { get; init; }

    /// <summary>Incremental first pass: valid routes are kept, only broken/new wires are re-routed.</summary>
    public TimeSpan InitialPass { get; init; }

    /// <summary>Full re-route with ordering permutations, run when the initial pass left failures.</summary>
    public TimeSpan OrderingCascade { get; init; }

    /// <summary>Full-ordering route attempts the cascade ran (mirrors LastOrderingAttemptCount).</summary>
    public int OrderingAttempts { get; init; }

    /// <summary>
    /// True when the ordering cascade stopped early after consecutive non-improving
    /// attempts (mirrors LastOrderingEarlyStopped) instead of running every ordering.
    /// </summary>
    public bool OrderingEarlyStopped { get; init; }

    /// <summary>Adaptive crossing-insertion pass (no-op without a crossing service).</summary>
    public TimeSpan CrossingInsertion { get; init; }

    /// <summary>Post-pass pulling the pin-side straight leads of auto routes onto their pins.</summary>
    public TimeSpan PinLeadCollapse { get; init; }

    /// <summary>Post-pass growing optical bend radii to the largest value the free space permits.</summary>
    public TimeSpan BendUpsizing { get; init; }

    /// <summary>Scan flagging sibling crossings no pass could resolve as blocked.</summary>
    public TimeSpan CrossingScan { get; init; }

    /// <summary>Contention-repair pass: bounded rip-up-and-reroute of contention-blocked wires.</summary>
    public TimeSpan ContentionRepair { get; init; }

    /// <summary>Rip-up-and-reroute attempts the contention-repair pass ran (0 when it never ran).</summary>
    public int ContentionRepairAttempts { get; init; }

    /// <summary>Contention-repair attempts accepted (blocked count strictly decreased, no new crossing).</summary>
    public int ContentionRepairAccepts { get; init; }

    /// <summary>True when cancellation cut the re-route short; the reported passes are what ran.</summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// Sum of the individually measured passes. Always ≤ <see cref="Total"/>; the gap is the
    /// uninstrumented glue (snapshots, bookkeeping) between passes.
    /// </summary>
    public TimeSpan MeasuredPassSum =>
        CrossingDissolution + InitialPass + OrderingCascade + CrossingInsertion
        + PinLeadCollapse + BendUpsizing + CrossingScan + ContentionRepair;
}
