using System.Text.Json;
using CAP_Core.Analysis;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Analysis;

/// <summary>
/// <c>validate_design</c>: runs the design checks the Checks tab runs — DRC-lite (waveguide
/// width/spacing/bend radius per PDK process), overlaps, unconnected pins, chip bounds, PDK and
/// process compatibility, chiplet interfaces and die assembly — and returns every finding with
/// severity, kind, position and the wire it concerns.
/// </summary>
public sealed class ValidateDesignTool : IAgentBridgeTool
{
    private const int DefaultLimit = 200;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ValidateDesignTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "validate_design";

    /// <inheritdoc/>
    public string Description =>
        "Run the design checks (DRC-lite per PDK process: waveguide width, spacing, bend radius; overlaps; " +
        "unconnected pins; chip bounds; PDK/process compatibility; chiplet interfaces and die assembly). Returns " +
        "counts per severity (problem / hint) and each finding with severity, kind, description, position (µm) and connection id.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            min_severity = new { type = "string", description = "'problem' to skip hints (often intentional, e.g. a spare port). Default: all." },
            limit = new { type = "integer", description = $"Maximum findings returned (default {DefaultLimit})." },
        },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var main = _session.Main;
        if (main == null)
            return AgentToolResult.Error("The main window is not ready yet.");
        if (!TryParseSeverity(ToolArguments.GetString(arguments, "min_severity"), out var minimum))
            return AgentToolResult.Error("min_severity must be hint or problem.");

        await main.RunDesignChecksCommand.ExecuteAsync(null);
        var findings = main.RightPanel.DesignValidation.AllFindings
            .Where(f => f.Severity >= minimum)
            .ToList();
        var limit = (int)(ToolArguments.GetDouble(arguments, "limit") ?? DefaultLimit);
        return AgentToolResult.Json(new
        {
            Total = findings.Count,
            BySeverity = findings.GroupBy(f => f.Severity.ToString().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count()),
            Findings = findings.Take(Math.Max(1, limit)).Select(f => new
            {
                Severity = f.Severity.ToString().ToLowerInvariant(),
                Kind = f.Type.ToString(),
                f.Description,
                X = Math.Round(f.X, 2),
                Y = Math.Round(f.Y, 2),
                ConnectionId = f.Connection?.Id,
            }).ToList(),
        });
    }

    private static bool TryParseSeverity(string? text, out DesignIssueSeverity severity)
    {
        severity = Enum.GetValues<DesignIssueSeverity>().Min();
        return text == null || Enum.TryParse(text, ignoreCase: true, out severity);
    }
}
