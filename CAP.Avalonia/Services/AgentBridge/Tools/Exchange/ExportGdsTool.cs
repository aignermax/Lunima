using System.Text.Json;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Exchange;

/// <summary>
/// <c>export_gds</c>: the File → Export Nazca path without dialogs — writes the Nazca Python
/// script and, when a Python environment with Nazca is available, runs it to produce the GDS.
/// Reports what the layout could not carry (blocked routes skipped, unresolved crossings,
/// placeholder geometry) so the agent can fix the design first.
/// </summary>
public sealed class ExportGdsTool : IAgentBridgeTool
{
    private const string ScriptExtension = ".py";

    private readonly AgentDesignSession _session;
    private readonly SimpleNazcaExporter _exporter;

    /// <summary>Creates the tool.</summary>
    public ExportGdsTool(AgentDesignSession session, SimpleNazcaExporter exporter)
    {
        _session = session;
        _exporter = exporter;
    }

    /// <inheritdoc/>
    public string Name => "export_gds";

    /// <inheritdoc/>
    public string Description =>
        "Export the layout: writes the Nazca script to path (.py) and, unless generate_gds=false, runs it with the " +
        "configured Python+Nazca to produce the .gds next to it. Returns script/gds paths and what the export " +
        "could not carry (skipped blocked routes, unresolved crossings, placeholder geometry).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Script path ending in .py (the GDS is written beside it)." },
            generate_gds = new { type = "boolean", description = "Run the script to produce the GDS. Default true." },
        },
        required = new[] { "path" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var fileOps = _session.FileOperations;
        var main = _session.Main;
        if (fileOps == null || main == null)
            return AgentToolResult.Error("The main window is not ready yet.");
        if (_session.Canvas.Components.Count == 0)
            return AgentToolResult.Error("The design is empty.");
        var path = ToolArguments.GetString(arguments, "path");
        if (path == null || !path.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase))
            return AgentToolResult.Error("Pass path ending in .py (e.g. /tmp/chip.py).");
        var scriptPath = Path.GetFullPath(path);
        var stem = Path.GetFileNameWithoutExtension(scriptPath);
        if (PythonModuleShadowing.ShadowsPythonModule(stem))
            return AgentToolResult.Error($"'{stem}.py' would shadow the Python module '{stem}'; choose another name (e.g. chip1.py).");

        var skipped = new List<string>();
        var unresolved = new List<string>();
        var warnings = new List<string>();
        var script = _exporter.Export(_session.Canvas, metalSpec: fileOps.MetalRoutingSpecProvider?.Invoke(),
            skippedConnections: skipped, unresolvedCrossings: unresolved,
            library: main.LeftPanel.AllTemplates, exportWarnings: warnings,
            designName: fileOps.CurrentFilePath != null ? Path.GetFileNameWithoutExtension(fileOps.CurrentFilePath) : null);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, script);

        string? gdsPath = null;
        string? gdsStatus = null;
        if (ToolArguments.GetBool(arguments, "generate_gds", fallback: true))
            (gdsPath, gdsStatus) = await GenerateGdsAsync(fileOps.GdsExport, scriptPath, context);

        return AgentToolResult.Json(new
        {
            ScriptPath = scriptPath,
            GdsPath = gdsPath,
            GdsStatus = gdsStatus,
            SkippedConnections = skipped,
            UnresolvedCrossings = unresolved,
            Warnings = warnings,
        });
    }

    private static async Task<(string? Path, string Status)> GenerateGdsAsync(
        ViewModels.Export.GdsExportViewModel gds, string scriptPath, AgentToolContext? context)
    {
        var token = context?.CancellationToken ?? default;
        if (!gds.IsEnvironmentReady)
        {
            if (string.IsNullOrEmpty(gds.CustomPythonPath)) await gds.SearchForPythonAsync(token);
            else await gds.CheckEnvironmentAsync(token);
        }
        if (!gds.IsEnvironmentReady)
            return (null, "Skipped: no Python with Nazca found. Install Nazca in Lunima's Python environment (Settings → Python), or run the script yourself.");

        var result = await gds.ExportScriptToGdsAsync(scriptPath, cancellationToken: token);
        return result.Success
            ? (result.GdsPath, result.Status)
            : (null, $"GDS generation failed: {result.ErrorMessage}");
    }
}
