using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Session;

/// <summary>
/// <c>load_example</c>: opens a shipped example design as an untitled copy through the same
/// loader as the Home screen's Examples list (so the user still gets the "unsaved changes"
/// prompt), waits for the post-load routing, and reports the result. Without a name it
/// lists the available examples.
/// </summary>
public sealed class LoadExampleTool : IAgentBridgeTool
{
    private const string DesignExtension = ".lun";

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public LoadExampleTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "load_example";

    /// <inheritdoc/>
    public string Description =>
        "Open a shipped example design (e.g. \"Logic Gate Counter 2-bit.lun\") as an untitled copy, replacing " +
        "the open design — Lunima asks the user first if there are unsaved changes. Call without a name to list " +
        "the available examples. Waits for routing and returns the blocked connections.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "Example file name (with or without .lun) or display name. Omit to list examples." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) =>
        ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        context?.ThrowIfStopped();
        var examples = _session.Examples.GetExamples();
        var name = ToolArguments.GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(name))
            return AgentToolResult.Json(new { Examples = examples.Select(e => Path.GetFileName(e.FilePath)).ToList() });

        var match = examples.FirstOrDefault(e => Matches(e, name.Trim()));
        if (match == null)
            return AgentToolResult.Error($"No example named '{name}'. Call load_example without a name to list them.");

        var fileOps = _session.FileOperations;
        if (fileOps == null)
            return AgentToolResult.Error("The main window is not ready yet.");
        CAP_Core.Documents.DocumentIdentity loadedDocument = default;
        bool CanProceed() { context?.ThrowIfStopped(); return true; }
        void OnOpened()
        {
            _session.LoadedExamplePath = match.FilePath;
            _session.Suggestions = null;
            loadedDocument = _session.Canvas.Document.CaptureIdentity();
        }
        if (!await fileOps.OpenDesignAsCopyAsync(match.FilePath, CanProceed, context, OnOpened))
        {
            context?.ThrowIfStopped();
            return AgentToolResult.Error("The example was not opened (the user cancelled the unsaved-changes prompt, or loading failed).");
        }

        await fileOps.PostLoadRouting;
        context?.ThrowIfStopped();
        var routed = await RoutingAwaiter.WaitForIdleAsync(_session.Canvas,
            cancellationToken: context?.CancellationToken ?? default);
        context?.ThrowIfStopped();
        if (!_session.Canvas.Document.IsCurrentIdentity(loadedDocument))
            return AgentToolResult.Error("The example was replaced while routing; its results are no longer current.");
        return AgentToolResult.Json(new
        {
            Loaded = Path.GetFileName(match.FilePath),
            ComponentCount = _session.Canvas.Components.Count,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }

    private static bool Matches(ExampleDesign example, string name)
    {
        var fileName = Path.GetFileName(example.FilePath);
        return string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, name + DesignExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(example.Name, name, StringComparison.OrdinalIgnoreCase);
    }
}
