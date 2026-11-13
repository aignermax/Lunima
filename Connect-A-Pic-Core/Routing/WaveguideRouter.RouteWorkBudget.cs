namespace CAP_Core.Routing;

public partial class WaveguideRouter
{
    /// <summary>Default for <see cref="MaxRouteNodesExpanded"/>.</summary>
    public const long DefaultMaxRouteNodesExpanded = 6_000_000;

    /// <summary>
    /// <see cref="MaxRouteNodesExpanded"/> that never runs out — for offline work (example bakes)
    /// that asks for searches an interactive routing pass cannot afford.
    /// </summary>
    public const long UnlimitedRouteNodesExpanded = long.MaxValue;

    /// <summary>
    /// Work budget of one <see cref="Route"/> call, in A* node expansions summed over every search
    /// the call runs: fine search and its retries, coarse retries and the breakout plans. The node
    /// budgets bound each search, but a wire with no path can run all of them in turn and keep a
    /// routing pass busy for a long time on a dense chip. When the budget runs out the searches
    /// stop as if cancelled and the wire gets its blocked fallback, so the pass moves on; the
    /// fallback is flagged <see cref="RoutedPath.SearchBudgetExhausted"/> and never remembered as
    /// unroutable, so a later pass tries the wire again. Counting expansions instead of time makes
    /// a design route the same on every machine.
    /// </summary>
    public long MaxRouteNodesExpanded { get; set; } = DefaultMaxRouteNodesExpanded;

    /// <summary>
    /// Node expansions the most recent <see cref="Route"/> call spent across all its searches
    /// (fine, coarse and breakout). Diagnostic surface for measuring routing cost per wire.
    /// </summary>
    public long LastRouteNodesExpanded { get; private set; }

    /// <summary>The budget of the <see cref="Route"/> call in progress; null outside one.</summary>
    private RouteWorkBudget? _routeBudget;
}
