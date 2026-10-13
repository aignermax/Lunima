using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

/// <summary>
/// <c>get_routing_timings</c>: per-phase wall-clock timings of the last routing pass
/// (<see cref="CAP_Core.Components.Connections.WaveguideConnectionManager.LastRoutingPassTimings"/>).
/// </summary>
public sealed class GetRoutingTimingsTool : IAgentBridgeTool
{
    private const int Decimals = 1;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public GetRoutingTimingsTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "get_routing_timings";

    /// <inheritdoc/>
    public string Description =>
        "Timings (milliseconds) of the last full routing pass, broken down by phase (initial pass, " +
        "ordering cascade, crossing insertion, pin-lead collapse, bend upsizing, contention repair), " +
        "plus attempt counters and whether the pass was cancelled.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var t = _session.Canvas.ConnectionManager.LastRoutingPassTimings;
        if (t == null)
            return Task.FromResult(AgentToolResult.Json(new { Available = false, IsRouting = _session.Canvas.Routing.IsRouting }));

        return Task.FromResult(AgentToolResult.Json(new
        {
            Available = true,
            IsRouting = _session.Canvas.Routing.IsRouting,
            TotalMs = Ms(t.Total),
            CrossingDissolutionMs = Ms(t.CrossingDissolution),
            InitialPassMs = Ms(t.InitialPass),
            OrderingCascadeMs = Ms(t.OrderingCascade),
            t.OrderingAttempts,
            t.OrderingEarlyStopped,
            CrossingInsertionMs = Ms(t.CrossingInsertion),
            PinLeadCollapseMs = Ms(t.PinLeadCollapse),
            BendUpsizingMs = Ms(t.BendUpsizing),
            CrossingScanMs = Ms(t.CrossingScan),
            ContentionRepairMs = Ms(t.ContentionRepair),
            t.ContentionRepairAttempts,
            t.ContentionRepairAccepts,
            t.WasCancelled,
        }));
    }

    private static double Ms(TimeSpan span) => Math.Round(span.TotalMilliseconds, Decimals);
}
