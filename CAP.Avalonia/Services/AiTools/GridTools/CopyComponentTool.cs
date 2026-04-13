using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that duplicates a component or group to a new position.
/// </summary>
public class CopyComponentTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public CopyComponentTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "copy_component";

    /// <inheritdoc/>
    public string Description =>
        "Duplicate a component or group to a new position. Preserves all internal structure, frozen paths, and settings. " +
        "Much faster than manually recreating circuits — use this for arrays, meshes, and symmetric designs.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            source_id = new { type = "string", description = "ID of the component or group to copy (use id from get_grid_state)" },
            x = new { type = "number", description = "Target X position for the copy in micrometers" },
            y = new { type = "number", description = "Target Y position for the copy in micrometers" },
            rotation = new { type = "integer", description = "Rotation in degrees (0, 90, 180, 270). Optional, omit to keep source rotation. Not applied to groups." }
        },
        required = new[] { "source_id", "x", "y" }
    };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        _gridService.CopyComponentAsync(
            AiInputReader.GetString(input, "source_id"),
            AiInputReader.GetDouble(input, "x"),
            AiInputReader.GetDouble(input, "y"),
            AiInputReader.GetInt(input, "rotation", -1));
}
