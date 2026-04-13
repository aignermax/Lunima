using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that places a photonic component on the grid at a given position.
/// </summary>
public class PlaceComponentTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public PlaceComponentTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "place_component";

    /// <inheritdoc/>
    public string Description =>
        "Place a photonic component on the grid at an approximate position. " +
        "The system finds the nearest valid placement automatically.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            component_type = new { type = "string", description = "Exact component type name from the PDK" },
            x = new { type = "number", description = "Target X center position in micrometers (0–5000)" },
            y = new { type = "number", description = "Target Y center position in micrometers (0–5000)" },
            rotation = new { type = "integer", description = "Rotation in degrees: 0, 90, 180, or 270 (optional, default 0)" }
        },
        required = new[] { "component_type", "x", "y" }
    };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        _gridService.PlaceComponentAsync(
            AiInputReader.GetString(input, "component_type"),
            AiInputReader.GetDouble(input, "x"),
            AiInputReader.GetDouble(input, "y"),
            AiInputReader.GetInt(input, "rotation", 0));
}
