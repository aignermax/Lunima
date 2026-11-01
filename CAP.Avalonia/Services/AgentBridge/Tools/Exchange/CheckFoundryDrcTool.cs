using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP_Core.Export.FoundryDrc;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Exchange;

/// <summary>
/// <c>check_foundry_drc</c>: sign-off style DRC — runs a foundry KLayout deck on a GDS (by default
/// the open design, exported first) and returns every rule with its violation count plus the
/// markers, each mapped back to the component it lies on. Complements the fast DRC-lite of
/// <c>validate_design</c>.
/// </summary>
public sealed class CheckFoundryDrcTool : IAgentBridgeTool
{
    private const int MaxMarkers = 100;

    private readonly AgentDesignSession _session;
    private readonly Func<AgentToolCatalog> _catalog;
    private readonly IFoundryDrcRunner _runner;

    /// <summary>Creates the tool; the catalog (for the export step) is resolved lazily.</summary>
    public CheckFoundryDrcTool(AgentDesignSession session, Func<AgentToolCatalog> catalog, IFoundryDrcRunner runner)
    {
        _session = session;
        _catalog = catalog;
        _runner = runner;
    }

    /// <inheritdoc/>
    public string Name => "check_foundry_drc";

    /// <inheritdoc/>
    public string Description =>
        "Run a foundry KLayout DRC deck (needs KLayout installed). deck: a shipped deck name (omit to list them; " +
        "e.g. cornerstone_sin300_drc) or a path to a .lydrc. gds_path: the GDS to check; omitted, the open design is " +
        "exported first (needs Python+Nazca). Returns each rule with its violation count and up to 100 markers " +
        "(µm, canvas frame) with the component they lie on.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            deck = new { type = "string", description = "Shipped deck name or path to a .lydrc deck." },
            gds_path = new { type = "string", description = "GDS to check (default: export the open design)." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
        Task.FromResult(AgentToolResult.Error("check_foundry_drc runs only through the tool catalog."));

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
    {
        var decks = _runner.ShippedDecks();
        var deck = ToolArguments.GetString(arguments, "deck");
        if (deck == null)
            return AgentToolResult.Json(new { Decks = decks.Keys.OrderBy(k => k).ToList(), Hint = "Pass deck=<name> or a .lydrc path." });
        var deckPath = decks.TryGetValue(deck, out var shipped) ? shipped : Path.GetFullPath(deck);

        var gds = ToolArguments.GetString(arguments, "gds_path");
        gds = gds != null ? Path.GetFullPath(gds) : await ExportOpenDesignAsync(context);
        if (gds == null)
            return AgentToolResult.Error("The open design could not be exported to GDS (see export_gds); pass gds_path instead.");

        var result = await _runner.RunAsync(gds, deckPath, context.CancellationToken);
        if (!result.Ran)
            return AgentToolResult.Error(result.Error!);
        return AgentToolResult.Json(new
        {
            Gds = gds,
            Deck = deckPath,
            Total = result.Markers.Count,
            Clean = result.Markers.Count == 0,
            Rules = result.Rules.OrderByDescending(r => r.Violations).ThenBy(r => r.Rule)
                .Select(r => new { r.Rule, r.Violations, r.Description }).ToList(),
            Markers = result.Markers.Take(MaxMarkers).Select(Locate).ToList(),
        });
    }

    private async Task<string?> ExportOpenDesignAsync(AgentToolContext context)
    {
        var script = Path.Combine(Path.GetTempPath(), $"lunima_drc_{Guid.NewGuid():N}.py");
        var export = await _catalog().InvokeWithinAsync("export_gds",
            JsonDocument.Parse(new JsonObject { ["path"] = script }.ToJsonString()).RootElement.Clone(), context);
        if (export.IsError)
            return null;
        var gds = JsonNode.Parse(export.Content[0].Text!)?["gds_path"]?.GetValue<string>();
        return gds != null && File.Exists(gds) ? gds : null;
    }

    /// <summary>The marker in the canvas frame (GDS is y-up, the canvas y-down) and the part it lies on.</summary>
    private object Locate(DrcMarker marker)
    {
        var x = (marker.MinX + marker.MaxX) / 2;
        var y = -(marker.MinY + marker.MaxY) / 2;
        var owner = _session.Canvas.Components.FirstOrDefault(c =>
        {
            var b = DesignDescriber.Describe(c).Bounds;
            return x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY;
        });
        return new
        {
            marker.Rule,
            X = Math.Round(x, 3),
            Y = Math.Round(y, 3),
            Width = Math.Round(marker.MaxX - marker.MinX, 3),
            Height = Math.Round(marker.MaxY - marker.MinY, 3),
            Component = owner?.Component.Identifier,
        };
    }
}
