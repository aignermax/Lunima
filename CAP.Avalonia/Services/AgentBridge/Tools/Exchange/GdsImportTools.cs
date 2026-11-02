using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.Services.GdsImport;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Exchange;

/// <summary>
/// <c>analyze_gds</c>: reads a GDS file without importing it — library, top-cell candidates
/// with their sub-cell instances, and every layer/datatype with polygon, path and text counts
/// (text layers usually carry port labels) — so an agent can pick the cell and layer roles.
/// </summary>
public sealed class AnalyzeGdsTool : IAgentBridgeTool
{
    private const int MaxChildCellsPerTop = 40;

    private readonly GdsImportService _import;

    /// <summary>Creates the tool.</summary>
    public AnalyzeGdsTool(GdsImportService import) => _import = import;

    /// <inheritdoc/>
    public string Name => "analyze_gds";

    /// <inheritdoc/>
    public string Description =>
        "Inspect a GDS file before importing: library name, cell count, top-cell candidates with the sub-cells " +
        "they instantiate (name × count), and the layer census (layer/datatype with polygon, path and text counts; " +
        "text usually marks port labels). Use the result to choose top_cell and the layers for import_gds.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new { path = new { type = "string", description = "Path of the .gds file." } },
        required = new[] { "path" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var path = GdsPath(arguments, out var error);
        if (path == null)
            return AgentToolResult.Error(error!);

        var analysis = await _import.AnalyzeAsync(path);
        return AgentToolResult.Json(new
        {
            Path = path,
            analysis.LibraryName,
            analysis.CellCount,
            TopCells = analysis.TopCells.Select(top => new
            {
                Name = top.CellName,
                InstanceCount = top.DirectInstanceCount,
                Children = ChildCells(analysis.Library, top.CellName),
            }).ToList(),
            Layers = analysis.LayerCensus.Select(l => new { l.Layer, l.Datatype, Polygons = l.PolygonCount, Paths = l.PathCount, Texts = l.TextCount }).ToList(),
        });
    }

    private static object? ChildCells(GdsLibrary? library, string cell) =>
        library != null && library.Cells.TryGetValue(cell, out var gdsCell)
            ? gdsCell.Elements.OfType<GdsReference>()
                .GroupBy(r => r.CellName)
                .OrderByDescending(g => g.Count())
                .Take(MaxChildCellsPerTop)
                .Select(g => new { Cell = g.Key, Count = g.Count() })
                .ToList()
            : null;

    /// <summary>Resolves and checks the <c>path</c> argument of a GDS tool.</summary>
    internal static string? GdsPath(JsonElement arguments, out string? error)
    {
        error = null;
        var path = ToolArguments.GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Pass the path of a .gds file.";
            return null;
        }
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            error = $"File not found: {full}";
        return error == null ? full : null;
    }
}

/// <summary>
/// <c>import_gds</c>: imports a GDS cell into the open design the way the GDS-import dialog does
/// — sub-cells become components with pins detected from port labels and waveguide ends, their
/// abutments and drawn routes become connections — and reports what was placed and connected.
/// </summary>
public sealed class ImportGdsTool : IAgentBridgeTool
{
    private const string Explode = "explode";
    private const string BlackBox = "black_box";

    private readonly AgentDesignSession _session;
    private readonly GdsImportService _import;
    private readonly GdsPlacementExecutor _placement;

    /// <summary>Creates the tool.</summary>
    public ImportGdsTool(AgentDesignSession session, GdsImportService import, GdsPlacementExecutor placement)
    {
        _session = session;
        _import = import;
        _placement = placement;
    }

    /// <inheritdoc/>
    public string Name => "import_gds";

    /// <inheritdoc/>
    public string Description =>
        "Import a GDS cell into the open design (run analyze_gds first). mode=explode turns each sub-cell instance " +
        "into a component (pins from port labels/waveguide ends, abutting pins and drawn routes become connections); " +
        "black_box imports the cell as one component. Layers are \"layer,datatype\" pairs separated by ';'. " +
        "Returns counts of placed/connected parts, warnings and the routing state.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Path of the .gds file." },
            top_cell = new { type = "string", description = "Cell to import (default: the first top-cell candidate)." },
            mode = new { type = "string", @enum = new[] { Explode, BlackBox }, description = "Default explode." },
            port_layers = new { type = "string", description = "Port-label layers, default \"1,10;501,1\"." },
            waveguide_layers = new { type = "string", description = "Waveguide layers, default \"1,0;1111,0\"." },
            metal_layers = new { type = "string", description = "Metal layers, default \"11,0;12,0;13,0\"." },
            reroute = new { type = "boolean", description = "Re-route imported connections with Lunima's router. Default false (keep drawn routes)." },
            auto_connect = new { type = "boolean", description = "Connect every remaining facing pin pair. Default false." },
            group = new { type = "boolean", description = "Wrap the imported parts in one group. Default true." },
        },
        required = new[] { "path" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var path = AnalyzeGdsTool.GdsPath(arguments, out var error);
        if (path == null)
            return AgentToolResult.Error(error!);
        var options = BuildOptions(arguments, out error);
        if (options == null)
            return AgentToolResult.Error(error!);
        var token = context?.CancellationToken ?? default;

        var analysis = await _import.AnalyzeAsync(path, token);
        var topCell = ToolArguments.GetString(arguments, "top_cell") ?? analysis.TopCellCandidates[0];
        if (!analysis.Library!.Cells.ContainsKey(topCell))
            return AgentToolResult.Error($"No cell '{topCell}'. Top-cell candidates: {string.Join(", ", analysis.TopCellCandidates)}.");

        var outcome = await _import.ImportAsync(path, topCell, options, null, token,
            preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes);
        var report = await _placement.ExecuteAsync(GdsPlacementPlan.FromOutcome(outcome), null, token,
            ToolArguments.GetBool(arguments, "reroute"), ToolArguments.GetBool(arguments, "auto_connect"),
            ToolArguments.GetBool(arguments, "group", fallback: true));
        if (report.ChipEnlargedToWidthUm is double width && report.ChipEnlargedToHeightUm is double height)
            _session.Main?.ChipSize.ApplyFromMicrometers(width, height);
        var routed = await RoutingAwaiter.WaitForIdleAsync(_session.Canvas, cancellationToken: token);

        return AgentToolResult.Json(new
        {
            TopCell = topCell,
            Mode = outcome.Mode.ToString(),
            Pdk = outcome.UserPdkName,
            ComponentTypes = outcome.RegisteredComponents.Count,
            Placed = report.PlacedCount,
            Connected = report.ConnectedCount,
            AutoConnected = report.AutoConnectedCount,
            report.GroupName,
            Warnings = outcome.Warnings.Concat(report.Warnings).Concat(report.ValidationWarnings).ToList(),
            report.SkippedPlacements,
            report.SkippedConnections,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }

    private static GdsHierarchyImportOptions? BuildOptions(JsonElement arguments, out string? error)
    {
        error = null;
        var ports = Layers(arguments, "port_layers", "1,10;501,1", ref error);
        var waveguides = Layers(arguments, "waveguide_layers", "1,0;1111,0", ref error);
        var metals = Layers(arguments, "metal_layers", "11,0;12,0;13,0", ref error);
        var mode = ToolArguments.GetString(arguments, "mode") ?? Explode;
        if (mode is not (Explode or BlackBox))
            error ??= $"mode must be {Explode} or {BlackBox}.";
        if (error != null)
            return null;

        return new GdsHierarchyImportOptions
        {
            Mode = mode == Explode ? GdsHierarchyImportMode.ExplodeHierarchy : GdsHierarchyImportMode.BlackBox,
            RouteLayers = waveguides!,
            MetalRouteLayers = metals!,
            PinDetection = new GdsPinDetectionOptions { PortLayers = ports!, WaveguideLayers = waveguides!, ElectricalLayers = metals! },
        };
    }

    private static List<(int Layer, int Datatype)>? Layers(JsonElement arguments, string key, string fallback, ref string? error)
    {
        var text = ToolArguments.GetString(arguments, key) ?? fallback;
        var parsed = ViewModels.GdsImport.GdsImportDialogViewModel.ParseLayerPairs(text);
        if (parsed == null)
            error ??= $"{key}: \"{text}\" is not a ';'-separated list of layer,datatype pairs.";
        return parsed;
    }
}
