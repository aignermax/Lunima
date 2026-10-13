using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

/// <summary>
/// <c>list_connections</c>: every waveguide connection with its endpoints, length,
/// blocked flag and failure reason — the way to find red (failed) routes.
/// </summary>
public sealed class ListConnectionsTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ListConnectionsTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "list_connections";

    /// <inheritdoc/>
    public string Description =>
        "List waveguide connections: id, endpoints as component.pin (leaf component) plus the top-level " +
        "start/end component to act on, length in µm, blocked flag (red route), failure_reason and whether " +
        "it is a piece of a waveguide crossing. Set blocked_only=true to list only failed routes.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            blocked_only = new { type = "boolean", description = "Only list blocked (failed) connections. Default false." },
            component = new { type = "string", description = "Only list connections touching this top-level component id (optional)." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var blockedOnly = ToolArguments.GetBool(arguments, "blocked_only");
        var component = ToolArguments.GetString(arguments, "component");

        IEnumerable<ConnectionInfo> connections = ConnectionSnapshot.Describe(_session);
        if (blockedOnly)
            connections = connections.Where(c => c.Blocked);
        if (!string.IsNullOrWhiteSpace(component))
            connections = connections.Where(c => c.StartComponent == component || c.EndComponent == component);

        var list = connections.ToList();
        return Task.FromResult(AgentToolResult.Json(new { Count = list.Count, Connections = list }));
    }
}
