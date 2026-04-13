using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that returns current light propagation values for all waveguide connections.
/// </summary>
public class GetLightValuesTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public GetLightValuesTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "get_light_values";

    /// <inheritdoc/>
    public string Description =>
        "Get current light propagation values (loss in dB, path length in µm) for all waveguide connections.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        Task.FromResult(_gridService.GetLightValues());
}
