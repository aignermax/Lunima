using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that ungroups a ComponentGroup back into individual components.
/// </summary>
public class UngroupTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public UngroupTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "ungroup";

    /// <inheritdoc/>
    public string Description =>
        "Ungroup a ComponentGroup back into individual components.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            group_id = new { type = "string", description = "ID of the group to ungroup" }
        },
        required = new[] { "group_id" }
    };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(_gridService.UngroupComponent(
            AiInputReader.GetString(input, "group_id")));
}
