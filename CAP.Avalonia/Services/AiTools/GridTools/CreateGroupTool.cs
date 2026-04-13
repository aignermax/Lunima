using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that groups multiple components into a ComponentGroup.
/// </summary>
public class CreateGroupTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public CreateGroupTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "create_group";

    /// <inheritdoc/>
    public string Description =>
        "Group multiple components together into a ComponentGroup. " +
        "Useful for organizing circuits and creating reusable subcircuits.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            component_ids = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Array of component IDs to group together (minimum 2 components)"
            },
            group_name = new { type = "string", description = "Optional name for the group" }
        },
        required = new[] { "component_ids" }
    };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(_gridService.CreateGroup(
            AiInputReader.GetStringArray(input, "component_ids"),
            AiInputReader.GetString(input, "group_name")));
}
