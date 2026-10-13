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
/// <param name="Stamp">Design fingerprint when the suggestions were made.</param>
/// <param name="Suggestions">Ranked best first.</param>
public sealed record RepairSuggestionSet(string TargetId, int Stamp, IReadOnlyList<RepairSuggestion> Suggestions)
{
    /// <summary>
    /// Fingerprint of what the suggestions depend on: every component's id and position and
    /// every connection's id.
    /// </summary>
    public static int ComputeStamp(AgentDesignSession session)
    {
        var hash = new HashCode();
        foreach (var vm in session.Canvas.Components.OrderBy(c => c.Component.Id))
        {
            hash.Add(vm.Component.Id);
            hash.Add(Math.Round(vm.X, 3));
            hash.Add(Math.Round(vm.Y, 3));
        }
        foreach (var id in session.Canvas.Connections.Select(c => c.Connection.Id).OrderBy(id => id))
            hash.Add(id);
        return hash.ToHashCode();
    }

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
