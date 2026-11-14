namespace CAP_Core.Routing;

/// <summary>
/// Deterministic work budget of one <see cref="WaveguideRouter.Route"/> call: every A* search the
/// call runs (fine phases and their retries, coarse retries, breakout middles) draws its node
/// expansions from one shared counter. Once the counter reaches the limit, <see cref="Token"/> is
/// cancelled, so the remaining searches stop exactly as if the caller had cancelled. Counting
/// expansions instead of elapsed time makes a route end at the same point on every machine,
/// however fast or busy it is.
/// </summary>
internal sealed class RouteWorkBudget : IDisposable
{
    private readonly CancellationTokenSource _stop;

    /// <summary>Creates a budget of <paramref name="maxNodesExpanded"/> expansions.</summary>
    /// <param name="maxNodesExpanded">Expansions the whole route may spend; 0 or less allows none.</param>
    /// <param name="callerToken">The caller's token; cancelling it also cancels <see cref="Token"/>.</param>
    internal RouteWorkBudget(long maxNodesExpanded, CancellationToken callerToken)
    {
        MaxNodesExpanded = Math.Max(0, maxNodesExpanded);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        if (IsExhausted)
            _stop.Cancel();
    }

    /// <summary>Expansions the whole route may spend.</summary>
    internal long MaxNodesExpanded { get; }

    /// <summary>Expansions charged so far.</summary>
    internal long NodesExpanded { get; private set; }

    /// <summary>True once the charged expansions reached <see cref="MaxNodesExpanded"/>.</summary>
    internal bool IsExhausted => NodesExpanded >= MaxNodesExpanded;

    /// <summary>Cancelled when the caller cancels or the budget is spent.</summary>
    internal CancellationToken Token => _stop.Token;

    /// <summary>
    /// The node limit a search may actually use: its own <paramref name="searchMaxNodes"/>, cut
    /// down to what the route has left.
    /// </summary>
    internal int Cap(int searchMaxNodes) =>
        (int)Math.Min(searchMaxNodes, Math.Max(0, MaxNodesExpanded - NodesExpanded));

    /// <summary>Charges a finished search's expansions; cancels <see cref="Token"/> once the budget is spent.</summary>
    internal void Charge(int nodesExpanded)
    {
        NodesExpanded += Math.Max(0, nodesExpanded);
        if (IsExhausted && !_stop.IsCancellationRequested)
            _stop.Cancel();
    }

    /// <inheritdoc />
    public void Dispose() => _stop.Dispose();
}
