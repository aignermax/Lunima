using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Session;

/// <summary>
/// <c>open_design</c>: opens a <c>.lun</c> file from disk (replacing the open design) and waits
/// for its routes. In the desktop app an unsaved design still triggers the save prompt.
/// </summary>
public sealed class OpenDesignTool : IAgentBridgeTool
{
    private const string DesignExtension = ".lun";

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public OpenDesignTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "open_design";

    /// <inheritdoc/>
    public string Description =>
        "Open a .lun design file (absolute path, or relative to the working directory), replacing the open " +
        "design. Waits for routing and returns the routing state. Save later with save_design.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new { path = new { type = "string", description = "Path of the .lun file." } },
        required = new[] { "path" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) =>
        ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        context?.ThrowIfStopped();
        var path = ToolArguments.GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
            return AgentToolResult.Error("Pass the path of a .lun file.");
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.EndsWith(DesignExtension, StringComparison.OrdinalIgnoreCase))
            return AgentToolResult.Error("Only .lun design files can be opened; import GDS with import_gds.");
        if (!File.Exists(fullPath))
            return AgentToolResult.Error($"File not found: {fullPath}");
        var fileOps = _session.FileOperations;
        if (fileOps == null)
            return AgentToolResult.Error("The main window is not ready yet.");

        CAP_Core.Documents.DocumentIdentity opened = default;
        bool CanProceed() { context?.ThrowIfStopped(); return true; }
        void OnOpened()
        {
            _session.LoadedExamplePath = null;
            _session.Suggestions = null;
            opened = _session.Canvas.Document.CaptureIdentity();
        }
        if (!await fileOps.LoadDesignFromPathAsync(fullPath, CanProceed, context, OnOpened))
        {
            context?.ThrowIfStopped();
            return AgentToolResult.Error("The design was not opened (unsaved-changes prompt cancelled, or the file failed to load — see the error console).");
        }

        await fileOps.PostLoadRouting;
        var routed = await RoutingAwaiter.WaitForIdleAsync(_session.Canvas, cancellationToken: context?.CancellationToken ?? default);
        context?.ThrowIfStopped();
        if (!_session.Canvas.Document.IsCurrentIdentity(opened))
            return AgentToolResult.Error("The design was replaced while routing; its results are no longer current.");
        return AgentToolResult.Json(new
        {
            Opened = fullPath,
            ComponentCount = _session.Canvas.Components.Count,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
