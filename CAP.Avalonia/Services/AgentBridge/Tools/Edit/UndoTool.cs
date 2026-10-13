using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// <c>undo</c>: undoes the most recent step of the shared undo history — exactly what the
/// user's Ctrl+Z would undo, whether the step came from the agent or the user.
/// </summary>
public sealed class UndoTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public UndoTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "undo";

    /// <inheritdoc/>
    public string Description =>
        "Undo the most recent edit in Lunima's undo history (the same step Ctrl+Z would undo — it may be " +
        "a user edit). Waits for re-routing and returns the blocked connections afterwards.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var description = _session.Commands.UndoDescription;
        if (!_session.Commands.Undo())
            return AgentToolResult.Error("Nothing to undo.");

        var routed = await RoutingAwaiter.RerouteAndWaitAsync(_session.Canvas);
        _session.Canvas.InvalidateSimulation();

        return AgentToolResult.Json(new
        {
            Undone = description,
            RemainingUndoSteps = _session.Commands.UndoCount,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
