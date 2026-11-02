using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Logic;

/// <summary>
/// <c>rename_signals</c>: gives the logic signals inside one placed cell new names, so several
/// instances of the same library cell can sit in one network (FA0: S→S0, FA1: S→S1, A→A1 …).
/// Signal names must be unique per output across a network; inputs with one name merge into
/// one network input. Undoable.
/// </summary>
public sealed class RenameSignalsTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public RenameSignalsTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "rename_signals";

    /// <inheritdoc/>
    public string Description =>
        "Rename logic signals inside one placed cell (a gate or a group of gates, recursively): renames = {\"S\": \"S1\", " +
        "\"A\": \"A1\", ...}. Needed when two instances of the same cell share a network — output signal names must be " +
        "unique, and input pins with the same name merge into one network input. Undoable; rebuild with logic_build.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Cell (group) id or unique name." },
            renames = new { type = "object", description = "Old signal name → new signal name." },
        },
        required = new[] { "id", "renames" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        if (_session.FindComponent(id)?.Component is not ComponentGroup cell)
            return AgentToolResult.Error($"No top-level cell (group) with id '{id}'.");
        if (!arguments.TryGetProperty("renames", out var map) || map.ValueKind != JsonValueKind.Object)
            return AgentToolResult.Error("Pass renames: {\"old\": \"new\", ...}.");
        var renames = map.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
            .ToDictionary(p => p.Name, p => p.Value.GetString()!.Trim());
        if (renames.Count == 0)
            return AgentToolResult.Error("renames maps old signal names to new non-empty names.");

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var command = new RenameSignalsCommand(cell, renames);
        edit.Commit(() => _session.Commands.ExecuteCommand(command));
        return command.RenamedPins == 0
            ? AgentToolResult.Error($"No gate pin in '{id}' carries any of {string.Join(", ", renames.Keys)}. See get_component (signal).")
            : AgentToolResult.Json(new { Cell = cell.GroupName, RenamedPins = command.RenamedPins, Renames = renames });
    }
}
