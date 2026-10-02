using CAP_Core.Routing;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Phase 2 of the full re-route: incremental routing failed for some connections, so
/// every connection is re-routed under different orderings and the best result is kept.
/// The cascade dominates the re-route wall-clock on large designs, so it is bounded twice
/// (issue #1296): the best attempt's routes are snapshotted and restored instead of
/// re-routing the best ordering a second time, and the remaining orderings are skipped
/// once <see cref="MaxNonImprovingOrderingAttempts"/> consecutive attempts brought no
/// improvement. Both bounds leave the kept routes identical to an unbounded cascade.
/// </summary>
public partial class WaveguideConnectionManager
{
    /// <summary>
    /// Consecutive full-ordering attempts without a lower failed count after which the
    /// ordering cascade stops early and keeps the best attempt's routes. Settable so
    /// tests can pin the stop behaviour independently of the default.
    /// </summary>
    internal int MaxNonImprovingOrderingAttempts { get; set; } = 2;

    /// <summary>
    /// True when the last ordering cascade stopped early because
    /// <see cref="MaxNonImprovingOrderingAttempts"/> consecutive attempts did not lower
    /// the best failed count. Diagnostic hook for tests and the route-bake census.
    /// </summary>
    public bool LastOrderingEarlyStopped { get; private set; }

    /// <summary>
    /// Phase 2 of the full re-route: incremental routing failed for some connections, so
    /// every connection is re-routed under different orderings and the best result is kept.
    /// </summary>
    private void RouteWithOrderingCascade(
        WaveguideRouter router,
        Action? progressCallback,
        CancellationToken cancellationToken)
    {
        // Snapshots: this runs on the routing thread while UI commands may mutate the list.
        var result = TryRouteInOrder(SnapshotConnections(), router, progressCallback, cancellationToken);
        LastOrderingAttemptCount++;
        if (cancellationToken.IsCancellationRequested) return;
        if (result.allValid) return;

        // When every failed wire is endpoint-blocked (a pin sealed in by a component
        // footprint), no ordering can free it — re-ordering cannot fix a footprint,
        // so the ordering retry storm is skipped and this attempt's routes are kept.
        if (AllFailuresEndpointBlocked(result.failedConnections))
            return;

        var bestOrder = SnapshotConnections();
        int bestFailedCount = result.failedCount;
        var bestPaths = SnapshotAttemptPaths(bestOrder);
        int nonImprovingAttempts = 0;

        var orderings = GenerateOrderings(SnapshotConnections(), MaxRoutingAttempts - 1);
        foreach (var ordering in orderings)
        {
            if (cancellationToken.IsCancellationRequested) return;
            result = TryRouteInOrder(ordering, router, progressCallback, cancellationToken);
            LastOrderingAttemptCount++;
            if (cancellationToken.IsCancellationRequested) return;

            if (result.allValid)
            {
                ReorderConnections(ordering);
                return;
            }

            // An attempt whose only failures are endpoint-blocked is the best any
            // ordering can achieve: those wires stay blocked under every ordering.
            if (AllFailuresEndpointBlocked(result.failedConnections))
            {
                ReorderConnections(ordering);
                return;
            }

            if (result.failedCount < bestFailedCount)
            {
                bestFailedCount = result.failedCount;
                bestOrder = ordering;
                bestPaths = SnapshotAttemptPaths(ordering);
                nonImprovingAttempts = 0;
            }
            else if (++nonImprovingAttempts >= MaxNonImprovingOrderingAttempts)
            {
                LastOrderingEarlyStopped = true;
                break;
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            ReorderConnections(bestOrder);
            RestoreBestAttemptRoutes(bestOrder, bestPaths, router);
        }
    }

    /// <summary>
    /// Deep copies of every routed path of one cascade attempt, keyed by connection. The
    /// next attempt re-routes the same connection objects, so the best attempt's geometry
    /// must be snapshotted before it is overwritten (same pattern as the contention
    /// repair's per-attempt snapshot).
    /// </summary>
    private static Dictionary<WaveguideConnection, RoutedPath> SnapshotAttemptPaths(
        List<WaveguideConnection> order) =>
        order.Where(c => c.RoutedPath != null)
            .ToDictionary(c => c, c => c.RoutedPath!.DeepCopy());

    /// <summary>
    /// Puts back the snapshotted routes of the best cascade attempt and re-registers their
    /// grid obstacles, replacing the second full re-route of the best ordering the cascade
    /// used to end with. Routing is deterministic, so the restored paths and the final
    /// obstacle set are identical to what that re-route produced — at zero routing cost.
    /// </summary>
    private void RestoreBestAttemptRoutes(
        List<WaveguideConnection> bestOrder,
        Dictionary<WaveguideConnection, RoutedPath> bestPaths,
        WaveguideRouter router)
    {
        var grid = router.PathfindingGrid!;
        grid.ClearAllWaveguideObstacles();
        foreach (var connection in bestOrder)
        {
            if (bestPaths.TryGetValue(connection, out var path))
                connection.ReplaceRoutedPath(path);
            else
                connection.RecalculateTransmission(router);

            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(
                    connection.Id,
                    connection.RoutedPath.Segments,
                    ObstacleWidthFor(connection));
            }
        }
    }
}
