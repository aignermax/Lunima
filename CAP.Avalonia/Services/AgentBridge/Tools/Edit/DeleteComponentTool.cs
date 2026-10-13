using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// <c>delete_component</c>: deletes a top-level component or group (with its connections)
/// through <see cref="DeleteComponentCommand"/>, so the user can undo it. Deleting a
/// waveguide-crossing component dissolves the crossing and restores the crossed nets.
/// </summary>
public sealed class DeleteComponentTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public DeleteComponentTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "delete_component";

    /// <inheritdoc/>
    public string Description =>
        "Delete a top-level component or group (id from list_components) together with its connections. " +
        "Deleting a crossing component removes the crossing and restores the two crossed connections. " +
        "Undoable. Waits for re-routing and returns the blocked connections afterwards.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new { id = new { type = "string", description = "Component id from list_components." } },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var vm = _session.FindComponent(id);
        if (vm == null)
            return AgentToolResult.Error($"No top-level component with id '{id}'. Use list_components.");
        if (vm.Component.IsLocked)
            return AgentToolResult.Error($"Component '{id}' is locked.");

        var connectionsBefore = _session.Canvas.Connections.Count;
        _session.Commands.ExecuteCommand(new DeleteComponentCommand(_session.Canvas, vm));
        var routed = await RoutingAwaiter.RerouteAndWaitAsync(_session.Canvas);
        _session.Canvas.InvalidateSimulation();

        return AgentToolResult.Json(new
        {
            Deleted = id,
            ConnectionsRemoved = Math.Max(0, connectionsBefore - _session.Canvas.Connections.Count),
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
