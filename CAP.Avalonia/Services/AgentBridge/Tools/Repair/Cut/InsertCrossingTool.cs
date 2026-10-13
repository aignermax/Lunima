using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>
/// <c>insert_crossing</c>: cuts a wire at a point by inserting the PDK crossing there (the Cut
/// tool's own command) and, optionally, routes a second wire straight through the crossing's
/// free ports — one undo step. Waits for re-routing and reports the blocked state.
/// </summary>
public sealed class InsertCrossingTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public InsertCrossingTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "insert_crossing";

    /// <inheritdoc/>
    public string Description =>
        "Insert a waveguide crossing that cuts connection_id at (x, y) µm — same checks as the Cut tool (an " +
        "axis-aligned straight with room for the crossing within 2 µm of the point, clear footprint; crossing " +
        "pieces may be cut too). With through_connection_id, that wire is re-docked through the crossing's free " +
        "ports so it passes straight through (use spots from list_cut_candidates). One undo step. Waits for routing.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            connection_id = new { type = "string", description = "The wire to cut (cut_connection_id)." },
            x = new { type = "number", description = "Cut point x in µm." },
            y = new { type = "number", description = "Cut point y in µm." },
            through_connection_id = new { type = "string", description = "Optional wire to route through the new crossing." },
        },
        required = new[] { "connection_id", "x", "y" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "connection_id") ?? "";
        var cut = _session.FindConnection(id)?.Connection;
        if (cut == null)
            return AgentToolResult.Error($"Unknown connection id '{id}'. Use list_connections.");
        var x = ToolArguments.GetDouble(arguments, "x");
        var y = ToolArguments.GetDouble(arguments, "y");
        if (x == null || y == null)
            return AgentToolResult.Error("Pass the cut point as x and y (µm).");

        var throughId = ToolArguments.GetString(arguments, "through_connection_id");
        var through = string.IsNullOrWhiteSpace(throughId) ? null : _session.FindConnection(throughId)?.Connection;
        if (!string.IsNullOrWhiteSpace(throughId) && through == null)
            return AgentToolResult.Error($"Unknown through_connection_id '{throughId}'.");

        var plan = CrossingCut.Create(_session, cut, x.Value, y.Value, through, out var error);
        if (plan == null)
            return AgentToolResult.Error("Cannot cut there: " + error);

        var routed = await RepairEdit.ExecuteAndRouteAsync(_session, plan.Command);
        if (!plan.Insert.Succeeded)
        {
            await RepairEdit.UndoAndRouteAsync(_session);
            return AgentToolResult.Error("The crossing could not be inserted there (the wire changed or the crossing lacks a port).");
        }

        return AgentToolResult.Json(new
        {
            Crossing = plan.Crossing.Identifier,
            RoutedThrough = plan.ThroughStep?.Applied ?? false,
            Warning = plan.ThroughStep is { Applied: false }
                ? "The crossing was inserted but the through wire could not be docked on its free ports."
                : null,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
