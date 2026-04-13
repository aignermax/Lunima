using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that returns the current photonic circuit grid state as JSON.
/// </summary>
public class GetGridStateTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public GetGridStateTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "get_grid_state";

    /// <inheritdoc/>
    public string Description =>
        "Get current photonic circuit grid state: placed components, connections, and available component types.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(_gridService.GetGridState());
}
