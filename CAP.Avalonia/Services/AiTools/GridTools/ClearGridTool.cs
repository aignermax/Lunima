using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that removes all components and connections from the grid.
/// </summary>
public class ClearGridTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public ClearGridTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "clear_grid";

    /// <inheritdoc/>
    public string Description =>
        "Remove all components and connections from the photonic circuit grid.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(_gridService.ClearGrid());
}
