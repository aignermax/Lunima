using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// <c>redo</c>: re-applies the most recently undone step of the shared history — exactly what the
/// user's Ctrl+Y would redo.
/// </summary>
public sealed class RedoTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public RedoTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "redo";

    /// <inheritdoc/>
    public string Description =>
        "Redo the step the last undo reverted (Ctrl+Y). " +
        "Waits for re-routing and returns the blocked connections afterwards.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var description = _session.Commands.RedoDescription;
        var routing = _session.Canvas.Routing;
        var restoredBefore = routing.RestoredGeometryVersion;
        if (!await routing.ExecuteHistoryChangeAsync(() => edit.Commit(_session.Commands.Redo),
                () => { edit.EnsureCurrent(); return _session.Commands.CanRedo; }))
            return AgentToolResult.Error("Nothing to redo.");

        var routed = routing.RestoredGeometryVersion != restoredBefore
            || await edit.RouteAsync();
        await edit.CompleteDerivedWorkAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);

        return AgentToolResult.Json(new
        {
            Redone = description,
            RemainingRedoSteps = _session.Commands.RedoCount,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
