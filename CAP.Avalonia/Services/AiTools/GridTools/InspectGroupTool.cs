using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that returns detailed internal structure of a ComponentGroup.
/// </summary>
public class InspectGroupTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public InspectGroupTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "inspect_group";

    /// <inheritdoc/>
    public string Description =>
        "Get detailed internal structure of a ComponentGroup: child components with types and positions, " +
        "internal waveguide connections, external pins, and nested group hierarchy. " +
        "Use this to understand what's inside a group.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            group_id = new { type = "string", description = "ID of the group to inspect (use id from get_grid_state)" }
        },
        required = new[] { "group_id" }
    };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(_gridService.InspectGroup(
            AiInputReader.GetString(input, "group_id")));
}
