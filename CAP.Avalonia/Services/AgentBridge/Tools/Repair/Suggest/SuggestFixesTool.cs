using System.Diagnostics;
using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>
/// <c>suggest_fixes</c>: for one blocked connection, generates deterministic repair candidates
/// (<see cref="RepairCandidateGenerator"/>), tries each on the live design and takes it back
/// (<see cref="RepairSandbox"/>) within a time budget, and returns them ranked by the result.
/// <c>apply_suggestion</c> then applies one by id.
/// </summary>
public sealed class SuggestFixesTool : IAgentBridgeTool
{
    /// <summary>Default time budget in seconds (a trial costs two routing passes).</summary>
    public const double DefaultBudgetSeconds = 240;

    /// <summary>Largest time budget accepted, in seconds.</summary>
    public const double MaxBudgetSeconds = 1800;

    private const int Decimals = 1;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public SuggestFixesTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "suggest_fixes";

    /// <inheritdoc/>
    public string Description =>
        "For one blocked connection: try repair candidates on the live design — cut a wire in the way and route the " +
        "blocked wire through the crossing (cut_through), slide a nearby crossing ±10/20 µm (move_crossing), shift a " +
        "straight of a pin-neighbour wire or of a wire running past the pins in 4 µm steps up to ±30 µm, widening the corridor first (shift_segment), move the component or gate group at either end in 4 µm steps up to ±30 µm, back from its pin first (nudge_component). " +
        "Each is executed, the design re-routed and measured, then undone. Returns suggestions " +
        "ranked by blocked count, crossings added, geometry change; apply one with apply_suggestion. Takes long: " +
        "each trial is two full routing passes; time_budget_s (default 240) bounds it. Clears the redo history.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            connection_id = new { type = "string", description = "The blocked connection (from list_connections blocked_only=true)." },
            time_budget_s = new { type = "number", description = "Stop starting new trials after this many seconds (default 240, max 1800)." },
            max_trials = new { type = "number", description = "Optional cap on the number of candidates tried." },
        },
        required = new[] { "connection_id" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "connection_id") ?? "";
        var target = _session.FindConnection(id)?.Connection;
        if (target == null)
            return AgentToolResult.Error($"Unknown connection id '{id}'. Use list_connections blocked_only=true.");
        if (!target.IsBlockedFallback)
            return AgentToolResult.Error("That connection is not blocked.");

        var budget = TimeSpan.FromSeconds(Math.Clamp(
            ToolArguments.GetDouble(arguments, "time_budget_s") ?? DefaultBudgetSeconds, 1, MaxBudgetSeconds));
        var maxTrials = (int)(ToolArguments.GetDouble(arguments, "max_trials") ?? int.MaxValue);

        var candidates = new RepairCandidateGenerator(_session, target).Generate();
        var baseline = RoutingMetrics.Measure(_session);
        var sandbox = new RepairSandbox(_session, target);
        var outcomes = await RunTrialsAsync(sandbox, candidates, baseline, budget, maxTrials);

        var ranked = RepairSuggestionSet.Rank(outcomes);
        _session.Suggestions = new RepairSuggestionSet(id, RepairSuggestionSet.ComputeStamp(_session), ranked);

        return AgentToolResult.Json(new
        {
            Connection = id,
            BlockedBefore = baseline.BlockedCount,
            CandidatesGenerated = candidates.Count,
            Tried = outcomes.Count,
            NotTried = candidates.Skip(outcomes.Count).Select(c => c.Description).ToList(),
            SandboxProblem = sandbox.Drift,
            Summary = Summarize(ranked, baseline, outcomes.Count),
            Suggestions = ranked.Select(s => Describe(s, baseline)).ToList(),
            NotApplicable = outcomes.Where(o => !o.Applicable).Select(o => new { o.Candidate.Description, Reason = o.Note }).ToList(),
        });
    }

    private static async Task<List<TrialOutcome>> RunTrialsAsync(RepairSandbox sandbox,
        IReadOnlyList<RepairCandidate> candidates, RoutingMetrics baseline, TimeSpan budget, int maxTrials)
    {
        var clock = Stopwatch.StartNew();
        var outcomes = new List<TrialOutcome>();
        foreach (var candidate in candidates)
        {
            if (outcomes.Count >= maxTrials || clock.Elapsed >= budget || sandbox.Drift != null)
                break;
            outcomes.Add(await sandbox.TryAsync(candidate, baseline));
        }
        return outcomes;
    }

    private static object Describe(RepairSuggestion suggestion, RoutingMetrics baseline)
    {
        var o = suggestion.Outcome;
        return new
        {
            suggestion.Id,
            o.Candidate.Kind,
            o.Candidate.Description,
            BlockedAfter = o.BlockedAfter,
            BlockedDelta = o.BlockedAfter - baseline.BlockedCount,
            TargetFixed = o.TargetFixed,
            Improves = Improves(o, baseline),
            o.CrossingsAdded,
            GeometryChangeUm = o.Candidate.GeometryChangeUm,
            Equivalent = o.Candidate.Arguments,
            Seconds = Math.Round(o.Seconds, Decimals),
            Explanation = Explain(o, baseline),
        };
    }

    /// <summary>Whether the trial left fewer blocked wires than before, or fixed the target without new blocked wires.</summary>
    public static bool Improves(TrialOutcome o, RoutingMetrics baseline) =>
        o.BlockedAfter < baseline.BlockedCount || (o.TargetFixed && o.BlockedAfter <= baseline.BlockedCount);

    private static string Summarize(IReadOnlyList<RepairSuggestion> ranked, RoutingMetrics baseline, int tried)
    {
        if (ranked.Count == 0)
            return $"None of the {tried} tried candidates could be applied.";
        return Improves(ranked[0].Outcome, baseline)
            ? $"{ranked[0].Id} improves the design: {Explain(ranked[0].Outcome, baseline)}"
            : $"None of the {tried} tried candidates reduced the blocked count; the best ({ranked[0].Id}) gives " +
              $"{ranked[0].Outcome.BlockedAfter} blocked (now {baseline.BlockedCount}). Try a larger time budget, " +
              "combinations with the direct tools, or Settings → Routing → Crossings (adaptive crossings).";
    }

    private static string Explain(TrialOutcome o, RoutingMetrics baseline)
    {
        var delta = o.BlockedAfter - baseline.BlockedCount;
        var effect = delta < 0 ? $"{-delta} fewer blocked wire(s)" : delta == 0 ? "no change in blocked count" : $"{delta} more blocked wire(s)";
        var target = o.TargetFixed ? "the wire routes" : "the wire stays blocked";
        return $"{effect}; {target}; {o.CrossingsAdded:+0;-0;0} crossing(s).";
    }
}
