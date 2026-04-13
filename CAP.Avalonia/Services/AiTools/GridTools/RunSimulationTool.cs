using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that runs the S-Matrix light propagation simulation.
/// </summary>
public class RunSimulationTool : IAiTool
{
    private readonly IAiGridService _gridService;

    /// <summary>Initializes the tool with the required grid service.</summary>
    public RunSimulationTool(IAiGridService gridService) => _gridService = gridService;

    /// <inheritdoc/>
    public string Name => "run_simulation";

    /// <inheritdoc/>
    public string Description =>
        "Run the S-Matrix light propagation simulation for the current circuit.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input) =>
        _gridService.RunSimulationAsync();
}
