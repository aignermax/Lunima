namespace CAP_Core.Routing;

/// <summary>
/// Deterministic work budget of one <see cref="WaveguideRouter.Route"/> call: every A* search the
/// call runs (fine phases and their retries, coarse retries, breakout middles) draws its node
/// expansions from one shared counter. Once the counter reaches the limit, <see cref="Token"/> is
/// cancelled, so the remaining searches stop exactly as if the caller had cancelled. Counting
/// expansions instead of elapsed time makes a route end at the same point on every machine,
/// however fast or busy it is.
/// <para>
/// A budget may draw from an enclosing one that spans many routes (a contention-repair pass charges
/// every route of every attempt to one pass budget): searches are then capped by whichever of the
/// two has less left, every expansion is charged to both, and spending the enclosing budget cancels
/// this one too.
/// </para>
/// </summary>
internal sealed class RouteWorkBudget : IDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly RouteWorkBudget? _enclosing;

    /// <summary>Creates a budget of <paramref name="maxNodesExpanded"/> expansions.</summary>
    /// <param name="maxNodesExpanded">Expansions the whole route may spend; 0 or less allows none.</param>
    /// <param name="callerToken">The caller's token; cancelling it also cancels <see cref="Token"/>.</param>
    internal RouteWorkBudget(long maxNodesExpanded, CancellationToken callerToken)
        : this(maxNodesExpanded, callerToken, enclosing: null)
    {
    }

    /// <summary>
    /// Creates a budget of <paramref name="maxNodesExpanded"/> expansions that also draws from
    /// <paramref name="enclosing"/> when one is given.
    /// </summary>
    /// <param name="maxNodesExpanded">Expansions the whole route may spend; 0 or less allows none.</param>
    /// <param name="callerToken">The caller's token; cancelling it also cancels <see cref="Token"/>.</param>
    /// <param name="enclosing">A budget spanning several routes that this one also charges, or null.</param>
    internal RouteWorkBudget(long maxNodesExpanded, CancellationToken callerToken, RouteWorkBudget? enclosing)
    {
        MaxNodesExpanded = Math.Max(0, maxNodesExpanded);
        _enclosing = enclosing;
        _stop = enclosing == null
            ? CancellationTokenSource.CreateLinkedTokenSource(callerToken)
            : CancellationTokenSource.CreateLinkedTokenSource(callerToken, enclosing.Token);
        if (IsExhausted)
            _stop.Cancel();
    }

    /// <summary>Expansions the whole route may spend.</summary>
    internal long MaxNodesExpanded { get; }

    /// <summary>Expansions charged so far.</summary>
    internal long NodesExpanded { get; private set; }

    /// <summary>
    /// True once the charged expansions reached <see cref="MaxNodesExpanded"/> or the enclosing
    /// budget is spent.
    /// </summary>
    internal bool IsExhausted => NodesExpanded >= MaxNodesExpanded || _enclosing?.IsExhausted == true;

    /// <summary>Cancelled when the caller cancels or the budget (or the enclosing one) is spent.</summary>
    internal CancellationToken Token => _stop.Token;

    /// <summary>
    /// The node limit a search may actually use: its own <paramref name="searchMaxNodes"/>, cut
    /// down to what this budget and the enclosing one have left.
    /// </summary>
    internal int Cap(int searchMaxNodes)
    {
        int enclosingCap = _enclosing?.Cap(searchMaxNodes) ?? searchMaxNodes;
        return (int)Math.Min(enclosingCap, Math.Max(0, MaxNodesExpanded - NodesExpanded));
    }

    /// <summary>
    /// Charges a finished search's expansions here and to the enclosing budget; cancels
    /// <see cref="Token"/> once either is spent.
    /// </summary>
    internal void Charge(int nodesExpanded)
    {
        NodesExpanded += Math.Max(0, nodesExpanded);
        _enclosing?.Charge(nodesExpanded);
        if (IsExhausted && !_stop.IsCancellationRequested)
            _stop.Cancel();
    }

    /// <inheritdoc />
    public void Dispose() => _stop.Dispose();
}
