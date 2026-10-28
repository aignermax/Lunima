namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>A ranked, tried repair the agent can apply by id.</summary>
/// <param name="Id">Suggestion id (<c>s1</c> is the best).</param>
/// <param name="Outcome">How the sandbox trial went.</param>
public sealed record RepairSuggestion(string Id, TrialOutcome Outcome);

/// <summary>
/// The suggestions of the last <c>suggest_fixes</c> call, valid only for the design state they
/// were computed on (<see cref="Stamp"/>): any edit in between makes them stale.
/// </summary>
/// <param name="TargetId">The blocked connection they repair.</param>
/// <param name="Stamp">Exact document revision and scope when the suggestions were made.</param>
/// <param name="Suggestions">Ranked best first.</param>
public sealed record RepairSuggestionSet(string TargetId, CAP_Core.Documents.DocumentRevisionStamp Stamp, IReadOnlyList<RepairSuggestion> Suggestions)
{
    /// <summary>
    /// Captures an exact, non-owning revision and scope stamp.
    /// </summary>
    public static CAP_Core.Documents.DocumentRevisionStamp ComputeStamp(AgentDesignSession session) =>
        session.Canvas.Document.CaptureRevision();

    /// <summary>
    /// Ranks applicable trials: fewest blocked wires, then fewest crossings added, then the
    /// smallest geometry change; candidates that could not be applied are left out.
    /// </summary>
    public static IReadOnlyList<RepairSuggestion> Rank(IEnumerable<TrialOutcome> outcomes) =>
        outcomes.Where(o => o.Applicable)
            .OrderBy(o => o.BlockedAfter)
            .ThenBy(o => o.CrossingsAdded)
            .ThenBy(o => o.Candidate.GeometryChangeUm)
            .Select((o, i) => new RepairSuggestion("s" + (i + 1), o))
            .ToList();
}
