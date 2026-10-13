using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

/// <summary>
/// <c>list_components</c>: the top-level components and groups on the canvas with their
/// ids, positions, rotation and bounds (µm).
/// </summary>
public sealed class ListComponentsTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ListComponentsTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "list_components";

    /// <inheritdoc/>
    public string Description =>
        "List the top-level components and groups on the canvas: id (use it with move_component / " +
        "delete_component), display name, type, position x/y (µm, top-left), rotation (degrees), " +
        "group membership, child count for groups, lock state and bounds. Optional name_filter " +
        "(case-insensitive substring of id, name or type).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            name_filter = new { type = "string", description = "Case-insensitive substring filter (optional)." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var filter = ToolArguments.GetString(arguments, "name_filter");
        var components = _session.Canvas.Components.Select(DesignDescriber.Describe);
        if (!string.IsNullOrWhiteSpace(filter))
            components = components.Where(c => Matches(c, filter));

        var list = components.ToList();
        return Task.FromResult(AgentToolResult.Json(new { Count = list.Count, Components = list }));
    }

    private static bool Matches(ComponentInfo info, string filter) =>
        info.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (info.Type?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
}
