using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>
/// <c>apply_suggestion</c>: applies one suggestion of the last <c>suggest_fixes</c> call as one
/// undo step, provided the design has not changed since (otherwise the suggestion is stale).
/// </summary>
public sealed class ApplySuggestionTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ApplySuggestionTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "apply_suggestion";

    /// <inheritdoc/>
    public string Description =>
        "Apply a suggestion from the last suggest_fixes call by id (\"s1\" is the best). Refused when its trial made " +
        "things worse (unless allow_worse=true) or when the design " +
        "changed since suggest_fixes ran — call suggest_fixes again. One undo step (two for reorder_neighbour); waits for routing and reports " +
        "the blocked connections.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Suggestion id, e.g. \"s1\"." },
            allow_worse = new { type = "boolean", description = "Apply even though its trial ended with more blocked wires than now. Default false." },
        },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var set = _session.Suggestions;
        if (set == null)
            return AgentToolResult.Error("No suggestions yet. Call suggest_fixes first.");
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var suggestion = set.Suggestions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (suggestion == null)
            return AgentToolResult.Error($"No suggestion '{id}'. Available: {string.Join(", ", set.Suggestions.Select(s => s.Id))}.");
        if (RepairSuggestionSet.ComputeStamp(_session) != set.Stamp)
            return AgentToolResult.Error("The design changed since suggest_fixes ran; call suggest_fixes again.");

        var current = RoutingMetrics.Measure(_session).BlockedCount;
        if (suggestion.Outcome.BlockedAfter > current && !ToolArguments.GetBool(arguments, "allow_worse"))
            return AgentToolResult.Error(
                $"Not applied: in its trial {suggestion.Id} ended with {suggestion.Outcome.BlockedAfter} blocked wires " +
                $"(now {current}). Pass allow_worse=true to apply it anyway, e.g. as a first step of a combination.");

        var (plan, error) = suggestion.Outcome.Candidate.Build();
        if (plan == null)
            return AgentToolResult.Error("The suggestion cannot be applied any more: " + error);

        var before = RoutingMetrics.Measure(_session);
        if (!await plan.ExecuteAndRouteAsync(_session))
        {
            await RepairEdit.UndoAndRouteAsync(_session);
            return AgentToolResult.Error("The edit did not take effect; nothing was changed.");
        }

        _session.Suggestions = null;
        var after = RoutingMetrics.Measure(_session);
        return AgentToolResult.Json(new
        {
            Applied = suggestion.Id,
            suggestion.Outcome.Candidate.Description,
            BlockedBefore = before.BlockedCount,
            BlockedAfter = after.BlockedCount,
            ExpectedBlockedAfter = suggestion.Outcome.BlockedAfter,
            CrossingsAdded = after.CrossingCount - before.CrossingCount,
            UndoSteps = plan.StepCount,
            UndoHint = plan.StepCount == 1
                ? "One undo step (undo tool or Ctrl+Z) reverts it."
                : $"{plan.StepCount} undo steps revert it.",
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
