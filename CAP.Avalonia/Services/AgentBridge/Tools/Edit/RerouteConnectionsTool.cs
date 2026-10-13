using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// <c>reroute_connections</c>: runs a routing pass, waits for it, and reports the blocked
/// state — of the requested connections, or of the whole design. The router always routes
/// all nets jointly (ordering and contention repair need the full picture), so the ids only
/// scope the report.
/// </summary>
public sealed class RerouteConnectionsTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public RerouteConnectionsTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "reroute_connections";

    /// <inheritdoc/>
    public string Description =>
        "Re-run the router and wait until it finishes, then report blocked (failed) connections. " +
        "The router always routes all nets jointly; pass ids to get the state of just those connections. " +
        "Routing itself is not an undo step.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            ids = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Connection ids (from list_connections) to report on. Omit for the whole design.",
            },
        },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var ids = ToolArguments.GetStringList(arguments, "ids");
        var unknown = ids.Where(id => _session.FindConnection(id) == null).ToList();
        if (unknown.Count > 0)
            return AgentToolResult.Error($"Unknown connection id(s): {string.Join(", ", unknown)}. Use list_connections.");

        var routed = await RoutingAwaiter.RerouteAndWaitAsync(_session.Canvas);
        _session.Canvas.InvalidateSimulation();

        if (ids.Count == 0)
            return AgentToolResult.Json(new { RoutingComplete = routed, Routing = ConnectionSnapshot.Report(_session) });

        var requested = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var connections = ConnectionSnapshot.Describe(_session).Where(c => requested.Contains(c.Id)).ToList();
        return AgentToolResult.Json(new
        {
            RoutingComplete = routed,
            BlockedCount = connections.Count(c => c.Blocked),
            Connections = connections,
        });
    }
}
