using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that connects two components with a waveguide.
/// </summary>
public class CreateConnectionTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public CreateConnectionTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "create_connection";

    /// <inheritdoc/>
    public string Description =>
        "Connect two placed components with a waveguide. Automatically selects compatible unconnected pins.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            from_component = new { type = "string", description = "ID of the source component (use id from get_grid_state)" },
            to_component = new { type = "string", description = "ID of the destination component (use id from get_grid_state)" }
        },
        required = new[] { "from_component", "to_component" }
    };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        _gridService.CreateConnectionAsync(
            AiInputReader.GetString(input, "from_component"),
            AiInputReader.GetString(input, "to_component"));
}
