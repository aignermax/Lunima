using CAP_Core.Routing;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Speculative parallel evaluation of the ordering cascade (issue #1360). Every cascade
/// attempt is an independent full re-route that starts from the same component-only grid
/// state and differs only in the wire ordering — the profile of the RAM 2×4 benchmark shows
/// the attempts are the dominant re-route cost and spend it almost entirely in A* searches
/// of wires that keep failing. Instead of running the orderings one after another on the
/// live connections, each ordering runs on its own isolated router clone
/// (<see cref="WaveguideRouter.CreateIsolatedRoutingClone"/>) with per-attempt proxy
/// connections, all in parallel; the sequential cascade's selection logic (first clean or
/// all-endpoint-blocked attempt wins, otherwise the lowest failed count with the
/// <see cref="MaxNonImprovingOrderingAttempts"/> early stop) is then replayed over the
/// precomputed outcomes and only the winning attempt is applied to the live connections.
/// The kept routes, the kept ordering, <see cref="LastOrderingAttemptCount"/> and
/// <see cref="LastOrderingEarlyStopped"/> are therefore identical to the sequential
/// cascade — only the wall-clock changes. The sequential path stays as the fallback for
/// routers the clone cannot reproduce (hierarchical pathfinding) and degenerate cases.
/// </summary>
public partial class WaveguideConnectionManager
{
    /// <summary>
    /// Whether the ordering cascade evaluates its orderings speculatively in parallel on
    /// isolated router clones instead of re-routing the live connections once per ordering.
    /// The selection replay guarantees an identical result either way; settable so tests can
    /// pin both paths against each other.
    /// </summary>
    internal bool UseParallelOrderingCascade { get; set; } = true;

    /// <summary>
    /// Runs every cascade ordering in parallel on isolated clones and applies the outcome the
    /// sequential cascade would have kept. Returns false when the cascade is not eligible
    /// (no alternative orderings exist) and the caller must run the sequential path. A
    /// cancelled pass applies nothing and reports handled: the live connections keep the
    /// incremental pass's complete routes.
    /// </summary>
    private bool RunOrderingCascadeInParallel(
        WaveguideRouter router,
        Action? progressCallback,
        CancellationToken cancellationToken)
    {
        var snapshot = SnapshotConnections();
        var orderings = GenerateOrderings(snapshot, MaxRoutingAttempts - 1);
        if (orderings.Count == 0)
            return false;

        var attempts = new List<List<WaveguideConnection>>(orderings.Count + 1) { snapshot };
        attempts.AddRange(orderings);

        var outcomes = new ParallelCascadeAttempt?[attempts.Count];
        var progressLock = new object();
        Action? guardedProgress = progressCallback == null
            ? null
            : () => { lock (progressLock) progressCallback(); };
        try
        {
            Parallel.For(0, attempts.Count, index =>
                outcomes[index] = RunIsolatedCascadeAttempt(
                    attempts[index], router, guardedProgress, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // The caller's token fired mid-evaluation; the cancellation check below applies nothing.
        }

        if (cancellationToken.IsCancellationRequested)
            return true;

        ReplayParallelCascade(outcomes!, router);
        return true;
    }

    /// <summary>
    /// One speculative attempt: routes proxies of the ordering's connections on an isolated
    /// router clone, exactly mirroring the sequential <c>TryRouteInOrder</c> failure
    /// accounting (blocked fallbacks and proper sibling crossings count as failures).
    /// Null when cancellation cut the attempt short — its half-routed proxies are discarded.
    /// </summary>
    private ParallelCascadeAttempt? RunIsolatedCascadeAttempt(
        List<WaveguideConnection> ordering,
        WaveguideRouter sourceRouter,
        Action? progressCallback,
        CancellationToken cancellationToken)
    {
        var router = sourceRouter.CreateIsolatedRoutingClone();
        var grid = router.PathfindingGrid!;
        var proxies = ordering.Select(CloneForIsolatedRouting).ToList();

        int failedCount = 0;
        var failedProxies = new List<WaveguideConnection>();
        var routedSoFar = new List<WaveguideConnection>();
        foreach (var proxy in proxies)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            proxy.RecalculateTransmission(router, cancellationToken: cancellationToken);
            RefreshStyledObstacleCollision(proxy, router);
            progressCallback?.Invoke();

            if (proxy.IsPathValid && proxy.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(proxy.Id, proxy.RoutedPath.Segments, ObstacleWidthFor(proxy));
                if (proxy.IsBlockedFallback || CrossesAnyRoutedSibling(proxy, routedSoFar))
                {
                    failedCount++;
                    failedProxies.Add(proxy);
                }
                routedSoFar.Add(proxy);
            }
            else
            {
                failedCount++;
                failedProxies.Add(proxy);
            }
        }

        return new ParallelCascadeAttempt(ordering, proxies, failedCount,
            AllFailuresEndpointBlocked(failedProxies));
    }

    /// <summary>
    /// The sequential cascade's selection logic replayed over the precomputed attempt
    /// outcomes: the first fully valid or all-endpoint-blocked attempt wins; otherwise the
    /// lowest failed count (earliest ordering on ties), with the early stop after
    /// <see cref="MaxNonImprovingOrderingAttempts"/> consecutive non-improving attempts.
    /// Only the selected attempt is applied to the live connections.
    /// </summary>
    private void ReplayParallelCascade(
        ParallelCascadeAttempt[] outcomes,
        WaveguideRouter router)
    {
        LastOrderingAttemptCount++;
        var best = outcomes[0];
        if (best.AllValid || best.AllEndpointBlocked)
        {
            ApplyCascadeAttempt(best, router);
            return;
        }

        int nonImprovingAttempts = 0;
        for (int i = 1; i < outcomes.Length; i++)
        {
            LastOrderingAttemptCount++;
            var outcome = outcomes[i];
            if (outcome.AllValid || outcome.AllEndpointBlocked)
            {
                ApplyCascadeAttempt(outcome, router);
                return;
            }

            if (outcome.FailedCount < best.FailedCount)
            {
                best = outcome;
                nonImprovingAttempts = 0;
            }
            else if (++nonImprovingAttempts >= MaxNonImprovingOrderingAttempts)
            {
                LastOrderingEarlyStopped = true;
                break;
            }
        }

        ApplyCascadeAttempt(best, router);
    }

    /// <summary>
    /// Publishes the selected attempt: the live connections take the attempt's order and its
    /// per-wire routes (adopting the discarded proxies' path objects), and the main grid's
    /// waveguide obstacles are re-registered from them — the same end state the sequential
    /// cascade reaches by routing the winning ordering live.
    /// </summary>
    private void ApplyCascadeAttempt(ParallelCascadeAttempt attempt, WaveguideRouter router)
    {
        ReorderConnections(attempt.Order);
        var grid = router.PathfindingGrid!;
        grid.ClearAllWaveguideObstacles();
        for (int i = 0; i < attempt.Order.Count; i++)
        {
            var connection = attempt.Order[i];
            var proxy = attempt.Proxies[i];
            if (proxy.RoutedPath != null)
            {
                connection.ReplaceRoutedPath(proxy.RoutedPath);
                connection.IsRouteFrozen = proxy.IsRouteFrozen;
                CopyManualEdits(proxy, connection);
            }
            else
            {
                connection.RecalculateTransmission(router);
            }

            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(
                    connection.Id,
                    connection.RoutedPath.Segments,
                    ObstacleWidthFor(connection));
            }
        }
    }

    /// <summary>
    /// A routing-only copy of a live connection: same pins, ids and routing-relevant state,
    /// but its own routed path and manual-edit dictionaries, so parallel attempts never
    /// share mutable state. A frozen or hand-edited route is deep-copied so the clone
    /// reproduces the keep-or-unfreeze decision the live connection would make.
    /// </summary>
    private static WaveguideConnection CloneForIsolatedRouting(WaveguideConnection connection)
    {
        var clone = new WaveguideConnection
        {
            Id = connection.Id,
            StartPin = connection.StartPin,
            EndPin = connection.EndPin,
            WidthMicrometers = connection.WidthMicrometers,
            BendRadiusMicrometers = connection.BendRadiusMicrometers,
            Type = connection.Type,
            IsRouteFrozen = connection.IsRouteFrozen,
            IsLocked = connection.IsLocked,
            PropagationLossDbPerCm = connection.PropagationLossDbPerCm,
            BendLossDbPer90Deg = connection.BendLossDbPer90Deg,
            DispersionModel = connection.DispersionModel,
            TargetLengthMicrometers = connection.TargetLengthMicrometers,
            LengthToleranceMicrometers = connection.LengthToleranceMicrometers,
            SourceGdsLayer = connection.SourceGdsLayer,
            SourceGdsDataType = connection.SourceGdsDataType,
        };
        CopyManualEdits(connection, clone);
        if (connection.RoutedPath != null)
            clone.RestoreCachedPath(connection.RoutedPath.DeepCopy());
        return clone;
    }

    /// <summary>Copies the manual bend-radius and segment-shift edits from one connection to another.</summary>
    private static void CopyManualEdits(WaveguideConnection source, WaveguideConnection target)
    {
        target.BendRadiusOverrides.Clear();
        foreach (var (index, radius) in source.BendRadiusOverrides)
            target.BendRadiusOverrides[index] = radius;
        target.StraightShiftOffsets.Clear();
        foreach (var (index, offset) in source.StraightShiftOffsets)
            target.StraightShiftOffsets[index] = offset;
    }

    /// <summary>
    /// The precomputed outcome of one speculative cascade attempt: the ordering (live
    /// connections), the per-position proxies that carry the attempt's routes, and the
    /// failure verdicts the sequential cascade bases its selection on.
    /// </summary>
    private sealed class ParallelCascadeAttempt
    {
        public ParallelCascadeAttempt(
            List<WaveguideConnection> order,
            List<WaveguideConnection> proxies,
            int failedCount,
            bool allEndpointBlocked)
        {
            Order = order;
            Proxies = proxies;
            FailedCount = failedCount;
            AllValid = failedCount == 0;
            AllEndpointBlocked = allEndpointBlocked;
        }

        public List<WaveguideConnection> Order { get; }
        public List<WaveguideConnection> Proxies { get; }
        public int FailedCount { get; }
        public bool AllValid { get; }
        public bool AllEndpointBlocked { get; }
    }
}
