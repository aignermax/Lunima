using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Analysis;

/// <summary>
/// <c>simulate</c>: runs the S-matrix simulation of the open design and returns structured
/// results — sources, received power per output, light lost at open ends, optionally the power
/// on every wire. Also updates the power overlay the canvas and screenshots show.
/// </summary>
public sealed class SimulateTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;
    private readonly SimulationService _simulation;

    /// <summary>Creates the tool.</summary>
    public SimulateTool(AgentDesignSession session, SimulationService simulation)
    {
        _session = session;
        _simulation = simulation;
    }

    /// <inheritdoc/>
    public string Name => "simulate";

    /// <inheritdoc/>
    public string Description =>
        "Run the S-matrix light simulation and return structured results: enabled sources, every output " +
        "(a light source with its laser off) with received power (linear, dB, transmission_db relative to the " +
        "injected power, phase), open ends where light escapes, and with include_connections the power on each wire. " +
        "Configure inputs/outputs with set_light_source first.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            include_connections = new { type = "boolean", description = "Also return the power on every wire. Default false." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var result = await _simulation.RunAsync(_session.Canvas, context?.CancellationToken ?? default, context);
        if (!result.Success)
            return AgentToolResult.Error($"Simulation did not run: {result.ErrorMessage}");
        return AgentToolResult.Json(SimulationReport.Build(_session.Canvas, result,
            ToolArguments.GetBool(arguments, "include_connections")));
    }
}
