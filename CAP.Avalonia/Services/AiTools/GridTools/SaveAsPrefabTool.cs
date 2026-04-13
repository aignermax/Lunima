using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that saves a ComponentGroup as a reusable prefab in the component library.
/// </summary>
public class SaveAsPrefabTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public SaveAsPrefabTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "save_as_prefab";

    /// <inheritdoc/>
    public string Description =>
        "Save a ComponentGroup as a reusable prefab/template in the component library. " +
        "The prefab will appear in the library panel.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            group_id = new { type = "string", description = "ID of the group to save as prefab" },
            prefab_name = new { type = "string", description = "Name for the prefab template" },
            description = new { type = "string", description = "Optional description of the prefab" }
        },
        required = new[] { "group_id", "prefab_name" }
    };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(_gridService.SaveGroupAsPrefab(
            AiInputReader.GetString(input, "group_id"),
            AiInputReader.GetString(input, "prefab_name"),
            AiInputReader.GetString(input, "description")));
}
