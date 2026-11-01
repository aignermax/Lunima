using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>create_group</c>: groups top-level components into one hierarchical cell (wires between
/// them move inside; wires leaving the group become its external pins). Undoable. Supersedes
/// the in-app assistant's non-undoable variant.
/// </summary>
public sealed class CreateGroupTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public CreateGroupTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "create_group";

    /// <inheritdoc/>
    public string Description =>
        "Group top-level components into one cell (a gate, an adder slice, a chip block): internal wires move " +
        "inside; open pins and wires leaving the group become its external pins. Make it a logic gate with make_gate " +
        "or store it in the library with save_as_prefab. Undoable.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            component_ids = new { type = "array", items = new { type = "string" }, description = "Ids or unique names of the components to group (at least 2)." },
            name = new { type = "string", description = "Group name (also the gate id in logic networks)." },
        },
        required = new[] { "component_ids" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var ids = ToolArguments.GetStringList(arguments, "component_ids");
        if (ids.Count < 2)
            return AgentToolResult.Error("Pass at least two component_ids.");
        var members = new List<ViewModels.Canvas.ComponentViewModel>();
        foreach (var id in ids)
        {
            var vm = _session.FindComponent(id);
            if (vm == null)
                return AgentToolResult.Error($"No top-level component with id '{id}'. Use list_components.");
            if (vm.Component is ComponentGroup { IsDie: true })
                return AgentToolResult.Error($"'{id}' is a die; dies cannot be grouped.");
            if (!members.Contains(vm))
                members.Add(vm);
        }

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var command = new CreateGroupCommand(_session.Canvas, members, ToolArguments.GetString(arguments, "name"));
        edit.Commit(() => _session.Commands.ExecuteCommand(command));
        var group = command.CreatedGroup;
        if (group == null)
            return AgentToolResult.Error("The group could not be created.");
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);
        var groupVm = _session.Canvas.Components.First(c => c.Component == group);
        return AgentToolResult.Json(new { Group = ComponentDescriber.Describe(_session, groupVm), RoutingComplete = routed });
    }
}

/// <summary><c>ungroup</c>: dissolves a group back into its members (undoable).</summary>
public sealed class UngroupTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public UngroupTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "ungroup";

    /// <inheritdoc/>
    public string Description => "Dissolve a group into its member components (one level). Undoable.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new { id = new { type = "string", description = "Group id or unique name." } },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        if (_session.FindComponent(id)?.Component is not ComponentGroup group)
            return AgentToolResult.Error($"No top-level group with id '{id}'.");
        if (group.IsDie)
            return AgentToolResult.Error($"'{id}' is a die; delete or move it with the die tools.");

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var members = group.ChildComponents.Select(c => c.Identifier).ToList();
        edit.Commit(() => _session.Commands.ExecuteCommand(new UngroupCommand(_session.Canvas, group)));
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);
        return AgentToolResult.Json(new { Ungrouped = id, Members = members, RoutingComplete = routed });
    }
}
