using System.Diagnostics;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Wall-clock instrumentation for a full re-route: each pass of
/// <see cref="RecalculateAllTransmissions"/> is measured separately and published as one
/// immutable <see cref="RoutingPassTimings"/> snapshot, so profiling (and the example route
/// bake census) can attribute the total routing time to the individual passes.
/// Instrumentation only — the routing behaviour is unchanged.
/// </summary>
public partial class WaveguideConnectionManager
{
    /// <summary>
    /// Per-pass wall-clock breakdown of the last <see cref="RecalculateAllTransmissions"/>
    /// pass, exposed like <see cref="LastOrderingAttemptCount"/>. Null until the first pass ran.
    /// </summary>
    public RoutingPassTimings? LastRoutingPassTimings { get; private set; }

    /// <summary>Measures one routing pass's wall-clock duration.</summary>
    private static TimeSpan MeasurePass(Action pass)
    {
        var watch = Stopwatch.StartNew();
        pass();
        return watch.Elapsed;
    }

    /// <summary>
    /// Mutable accumulator for the per-pass durations of one in-flight re-route; published as
    /// an immutable <see cref="RoutingPassTimings"/> when the pass ends. The counts are copied
    /// from the public diagnostic counters right after their pass ran, so a pass skipped by
    /// cancellation reports 0 instead of a stale value from a previous re-route.
    /// </summary>
    private sealed class RoutingPassTimingsBuilder
    {
        public TimeSpan CrossingDissolution;
        public TimeSpan InitialPass;
        public TimeSpan OrderingCascade;
        public TimeSpan CrossingInsertion;
        public TimeSpan PinLeadCollapse;
        public TimeSpan BendUpsizing;
        public TimeSpan CrossingScan;
        public TimeSpan ContentionRepair;
        public int ContentionRepairAttempts;
        public int ContentionRepairAccepts;

        public RoutingPassTimings Build(TimeSpan total, int orderingAttempts, bool wasCancelled) =>
            new()
            {
                Total = total,
                CrossingDissolution = CrossingDissolution,
                InitialPass = InitialPass,
                OrderingCascade = OrderingCascade,
                OrderingAttempts = orderingAttempts,
                CrossingInsertion = CrossingInsertion,
                PinLeadCollapse = PinLeadCollapse,
                BendUpsizing = BendUpsizing,
                CrossingScan = CrossingScan,
                ContentionRepair = ContentionRepair,
                ContentionRepairAttempts = ContentionRepairAttempts,
                ContentionRepairAccepts = ContentionRepairAccepts,
                WasCancelled = wasCancelled,
            };
    }
}
