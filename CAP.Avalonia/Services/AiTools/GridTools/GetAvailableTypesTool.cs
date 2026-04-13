using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that returns all available component type names from loaded PDKs.
/// </summary>
public class GetAvailableTypesTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public GetAvailableTypesTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "get_available_types";

    /// <inheritdoc/>
    public string Description =>
        "Get a full list of all available component types from loaded PDKs.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(JsonSerializer.Serialize(_gridService.GetAvailableComponentTypes()));
}
