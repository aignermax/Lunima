using System.Text.Json;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Dies;

/// <summary>
/// <c>delete_die</c>: removes a die (chiplet) with its parts and links through
/// <see cref="DeleteDieCommand"/>, which also refreshes the assembly's bounds and interfaces.
/// </summary>
public sealed class DeleteDieTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public DeleteDieTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "delete_die";

    /// <inheritdoc/>
    public string Description => "Delete a die (chiplet) with everything on it and its links. Undoable.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new { die = new { type = "string", description = "Die name (see list_dies)." } },
        required = new[] { "die" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var name = ToolArguments.GetString(arguments, "die");
        var die = DieToolSupport.FindDie(_session, name);
        var vm = die == null ? null : _session.Canvas.Components.FirstOrDefault(c => c.Component == die);
        if (vm == null)
            return AgentToolResult.Error($"No die '{name}'. Use list_dies.");

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        edit.Commit(() => _session.Commands.ExecuteCommand(new DeleteDieCommand(_session.Canvas, vm)));
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);
        return AgentToolResult.Json(new
        {
            Deleted = name,
            RemainingDies = _session.Canvas.Dies.RootDies.Count,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
