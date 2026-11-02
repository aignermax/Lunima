using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>save_as_prefab</c>: stores a group (a cell or a gate) in the component library so it can
/// be placed again with <c>place_component</c> — in this design or any other. This is how
/// hierarchical designs grow: gate → adder slice → ALU. Supersedes the in-app assistant's
/// variant, which only found groups by their generated id and reported failures as success.
/// </summary>
public sealed class SavePrefabTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public SavePrefabTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "save_as_prefab";

    /// <inheritdoc/>
    public string Description =>
        "Save a group (cell or gate, with its gate definition) to the user's component library under a name; " +
        "place copies with place_component type=<name>, also in other designs. A name that exists is refused " +
        "unless replace=true. Persists across sessions; remove with delete_prefab.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            group_id = new { type = "string", description = "Group id or unique name." },
            prefab_name = new { type = "string", description = "Library name (defaults to the group name)." },
            description = new { type = "string", description = "Optional description." },
            replace = new { type = "boolean", description = "Replace a library entry with the same name. Default false." },
        },
        required = new[] { "group_id" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "group_id") ?? "";
        if (_session.FindComponent(id)?.Component is not ComponentGroup { IsDie: false } group)
            return Task.FromResult(AgentToolResult.Error($"No top-level group with id '{id}'."));
        var library = _session.Main?.LeftPanel.ComponentLibrary;
        if (library == null)
            return Task.FromResult(AgentToolResult.Error("The component library is not available."));

        var name = ToolArguments.GetString(arguments, "prefab_name") ?? group.GroupName;
        var existing = library.GetLibraryManager().Templates
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!ToolArguments.GetBool(arguments, "replace"))
                return Task.FromResult(AgentToolResult.Error($"The library already has '{name}'. Pass replace=true or another prefab_name."));
            library.RemoveTemplate(existing);
        }

        var command = new SaveGroupToLibraryCommand(library, new GroupPreviewGenerator(), group, name,
            ToolArguments.GetString(arguments, "description"));
        command.Execute();
        var template = command.CreatedTemplate!;
        return Task.FromResult(AgentToolResult.Json(new
        {
            Saved = template.Name,
            Width = template.WidthMicrometers,
            Height = template.HeightMicrometers,
            Components = template.ComponentCount,
            IsGate = group.TruthTablePinAssignment != null,
            Pins = group.ExternalPins.Select(p => p.Name).ToList(),
        }));
    }
}

/// <summary><c>delete_prefab</c>: removes a user group from the component library.</summary>
public sealed class DeletePrefabTool : IAgentBridgeTool
{
    private const string UserSource = "User";

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public DeletePrefabTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "delete_prefab";

    /// <inheritdoc/>
    public string Description => "Remove a user group/gate from the component library (placed instances stay).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new { prefab_name = new { type = "string", description = "Library name." } },
        required = new[] { "prefab_name" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var name = ToolArguments.GetString(arguments, "prefab_name") ?? "";
        var library = _session.Main?.LeftPanel.ComponentLibrary;
        var template = library?.GetLibraryManager().Templates
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (library == null || template == null)
            return Task.FromResult(AgentToolResult.Error($"No library group named '{name}'."));
        if (template.Source != UserSource)
            return Task.FromResult(AgentToolResult.Error($"'{name}' ships with a PDK and cannot be deleted."));
        library.RemoveTemplate(template);
        return Task.FromResult(AgentToolResult.Json(new { Deleted = template.Name }));
    }
}
