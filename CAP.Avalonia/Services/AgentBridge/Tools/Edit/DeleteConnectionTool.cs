using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// <c>delete_connection</c>: removes one waveguide connection through
/// <see cref="DeleteConnectionCommand"/> (undoable). Deleting a crossing piece dissolves
/// the crossing; undo restores the original, unsplit connection.
/// </summary>
public sealed class DeleteConnectionTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public DeleteConnectionTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "delete_connection";

    /// <inheritdoc/>
    public string Description =>
        "Delete one waveguide connection (id from list_connections). Deleting a crossing piece dissolves " +
        "that crossing. Undoable. Waits for re-routing and returns the blocked connections afterwards.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new { id = new { type = "string", description = "Connection id from list_connections." } },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var vm = _session.FindConnection(id);
        if (vm == null)
            return AgentToolResult.Error($"No connection with id '{id}'. Use list_connections.");
        if (vm.Connection.IsLocked)
            return AgentToolResult.Error($"Connection '{id}' is locked.");

        var description = DesignDescriber.Describe(vm.Connection, isCrossingPiece: false);
        edit.Commit(() => _session.Commands.ExecuteCommand(new DeleteConnectionCommand(_session.Canvas, vm)));
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);

        return AgentToolResult.Json(new
        {
            Deleted = id,
            description.Start,
            description.End,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
