using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Session;

/// <summary>
/// <c>new_design</c>: replaces the open design with an empty, untitled one — the File → New
/// path, so the desktop app still asks about unsaved changes.
/// </summary>
public sealed class NewDesignTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public NewDesignTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "new_design";

    /// <inheritdoc/>
    public string Description =>
        "Start a new, empty, untitled design (File → New). Then place components, connect them and save_design " +
        "with a path.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(context);

    private async Task<AgentToolResult> ExecuteCoreAsync(AgentToolContext? context)
    {
        context?.ThrowIfStopped();
        var fileOps = _session.FileOperations;
        if (fileOps == null)
            return AgentToolResult.Error("The main window is not ready yet.");
        bool CanProceed() { context?.ThrowIfStopped(); return true; }
        if (!await fileOps.TryNewProjectAsync(CanProceed))
        {
            context?.ThrowIfStopped();
            return AgentToolResult.Error("No new design was created (the unsaved-changes prompt was cancelled).");
        }

        _session.LoadedExamplePath = null;
        _session.Suggestions = null;
        return AgentToolResult.Json(new { Created = true, ComponentCount = _session.Canvas.Components.Count });
    }
}
