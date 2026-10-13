using System.Text.Json;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

/// <summary>
/// <c>get_design_summary</c>: file name and headline counts of the open design — the
/// first call an agent makes to orient itself.
/// </summary>
public sealed class GetDesignSummaryTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public GetDesignSummaryTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "get_design_summary";

    /// <inheritdoc/>
    public string Description =>
        "Summary of the design open in Lunima: file name, unsaved flag, counts of components, groups, " +
        "connections, blocked (red, failed) connections and crossing pieces, routing state and undo depth.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var canvas = _session.Canvas;
        var fileOps = _session.FileOperations;
        var connections = ConnectionSnapshot.Describe(_session);

        return Task.FromResult(AgentToolResult.Json(new
        {
            FileName = fileOps?.CurrentFilePath is { } path ? Path.GetFileName(path) : null,
            FilePath = fileOps?.CurrentFilePath,
            HasUnsavedChanges = fileOps?.HasUnsavedChanges ?? false,
            ComponentCount = canvas.Components.Count,
            GroupCount = canvas.Components.Count(c => c.Component is ComponentGroup),
            ConnectionCount = connections.Count,
            BlockedCount = connections.Count(c => c.Blocked),
            CrossingPieceCount = connections.Count(c => c.IsCrossingPiece),
            IsRouting = canvas.Routing.IsRouting,
            UndoCount = _session.Commands.UndoCount,
            NextUndo = _session.Commands.UndoDescription,
        }));
    }
}
