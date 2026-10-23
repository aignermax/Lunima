using CAP.Avalonia.Commands;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>A built repair edit and how to tell whether it actually changed the design.</summary>
/// <param name="Command">The undoable command.</param>
/// <param name="Applied">After execution: did the command take effect (some commands degrade to a no-op)?</param>
/// <param name="FollowUps">Further commands, each run after a full routing pass of the previous step
/// (e.g. re-adding a wire after the blocked one routed). Each is its own undo step.</param>
public sealed record RepairEditPlan(IUndoableCommand Command, Func<bool> Applied, IReadOnlyList<IUndoableCommand>? FollowUps = null)
{
    /// <summary>Number of undo steps the edit takes.</summary>
    public int StepCount => 1 + (FollowUps?.Count ?? 0);

    /// <summary>Runs the command, then each follow-up after a routing pass; ends with a routing pass.</summary>
    public async Task<bool> ExecuteAndRouteAsync(AgentDesignSession session)
    {
        await using var operation = new AgentDocumentOperation(session.Canvas);
        return await ExecuteAndRouteAsync(session, operation);
    }

    /// <summary>Retains the caller's permission across every follow-up and routing pass.</summary>
    public async Task<bool> ExecuteAndRouteAsync(AgentDesignSession session, AgentDocumentOperation operation)
    {
        return await ExecuteStepsAsync(session, operation.Commit, async () => { await operation.RouteAsync(); });
    }

    internal async Task<bool> ExecuteStepsAsync(AgentDesignSession session, Action<Action> commit, Func<Task> route)
    {
        commit(() => session.Commands.ExecuteCommand(Command));
        if (!Applied()) return false;
        foreach (var followUp in FollowUps ?? Array.Empty<IUndoableCommand>())
        {
            await route();
            commit(() => session.Commands.ExecuteCommand(followUp));
        }
        await route();
        commit(session.Canvas.InvalidateSimulation);
        return true;
    }
}

/// <summary>
/// One repair idea for a blocked wire. The edit is built fresh for every run (sandbox trial
/// and apply), because commands own their state.
/// </summary>
/// <param name="Kind"><c>cut_through</c>, <c>move_crossing</c>, <c>shift_segment</c> or <c>nudge_component</c>.</param>
/// <param name="Description">Human-readable summary, also shown to the agent.</param>
/// <param name="GeometryChangeUm">How far geometry moves (µm) — the last ranking key.</param>
/// <param name="Arguments">The equivalent direct tool call (tool name and arguments), so the agent can replay or vary it.</param>
/// <param name="Instruction">Detached edit parameters, resolved against the receiving session.</param>
public sealed record RepairCandidate(
    string Kind, string Description, double GeometryChangeUm, object Arguments,
    RepairInstruction Instruction)
{
    public (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session) => Instruction.Build(session);
}

/// <summary>Result of trying one candidate in the sandbox.</summary>
/// <param name="Candidate">What was tried.</param>
/// <param name="Applicable">Whether the edit could be applied at all.</param>
/// <param name="Note">Why it was not applicable, or other remarks.</param>
/// <param name="BlockedAfter">Blocked wires after the edit and re-route.</param>
/// <param name="CrossingsAdded">Crossing components added (negative: removed).</param>
/// <param name="TargetFixed">Whether the wire being repaired is no longer blocked.</param>
/// <param name="Seconds">Wall time of the private trial including its routing passes.</param>
public sealed record TrialOutcome(
    RepairCandidate Candidate, bool Applicable, string? Note, int BlockedAfter, int CrossingsAdded,
    bool TargetFixed, double Seconds);
