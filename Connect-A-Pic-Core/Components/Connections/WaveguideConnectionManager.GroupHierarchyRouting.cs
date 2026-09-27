using CAP_Core.Routing;
using CAP_Core.Routing.GroupHierarchyRouting;

namespace CAP_Core.Components.Connections;

public partial class WaveguideConnectionManager
{
    /// <summary>
    /// Above this connection count, a failed hierarchical pass is NOT followed by the
    /// design-wide ordering search: permuting hundreds of wires costs hours of CPU
    /// (issue #1175) while the hierarchical result is already the local optimum.
    /// </summary>
    private const int MaxConnectionsForGlobalOrderingSearch = 60;

    /// <summary>
    /// Full re-route following a <see cref="GroupHierarchyRoutePlan"/>: buckets are routed
    /// deepest group first, each bucket's routes become fixed obstacles for the shallower
    /// levels, and ordering retries permute only the wires of one bucket. Returns the
    /// number of failed wires and the concatenated bucket order actually routed —
    /// re-routing that order flat reproduces this pass's geometry exactly.
    /// </summary>
    internal (int FailedCount, List<WaveguideConnection> Order) TryRouteHierarchical(
        GroupHierarchyRoutePlan plan,
        WaveguideRouter router,
        Action? progressCallback,
        CancellationToken cancellationToken)
    {
        router.PathfindingGrid!.ClearAllWaveguideObstacles();

        var routedSoFar = new List<WaveguideConnection>();
        var finalOrder = new List<WaveguideConnection>();
        int totalFailed = 0;

        foreach (var bucket in plan.Buckets)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var (order, failed) = RouteBucketWithRetries(
                bucket, router, routedSoFar, progressCallback, cancellationToken);

            routedSoFar.AddRange(order.Where(c => c.IsPathValid && c.RoutedPath != null));
            finalOrder.AddRange(order);
            totalFailed += failed;
        }

        return (totalFailed, finalOrder);
    }

    /// <summary>
    /// Routes one bucket, retrying with alternative orderings of ONLY this bucket's
    /// wires when some fail. Earlier buckets' routes stay registered as obstacles
    /// throughout. Ends with the best ordering's geometry on the grid.
    /// </summary>
    private (List<WaveguideConnection> Order, int FailedCount) RouteBucketWithRetries(
        GroupRoutingBucket bucket,
        WaveguideRouter router,
        IReadOnlyList<WaveguideConnection> routedBefore,
        Action? progressCallback,
        CancellationToken cancellationToken)
    {
        var bestOrder = bucket.Connections;
        int bestFailed = RouteBucketOnce(bestOrder, router, routedBefore, progressCallback, cancellationToken);
        if (bestFailed == 0 || bestOrder.Count <= 1 || cancellationToken.IsCancellationRequested)
            return (bestOrder, bestFailed);

        foreach (var ordering in GenerateOrderings(bucket.Connections, MaxRoutingAttempts - 1))
        {
            if (cancellationToken.IsCancellationRequested)
                return (bestOrder, bestFailed);

            int failed = RouteBucketOnce(ordering, router, routedBefore, progressCallback, cancellationToken);
            if (failed == 0)
                return (ordering, 0);
            if (failed < bestFailed)
            {
                bestFailed = failed;
                bestOrder = ordering;
            }
        }

        if (!cancellationToken.IsCancellationRequested)
            bestFailed = RouteBucketOnce(bestOrder, router, routedBefore, progressCallback, cancellationToken);
        return (bestOrder, bestFailed);
    }

    /// <summary>
    /// Routes the bucket's wires once in the given order. Only this bucket's previous
    /// obstacles are cleared first; a wire counts as failed when it has no valid path,
    /// is a blocked fallback, or geometrically crosses any wire routed so far
    /// (earlier buckets included).
    /// </summary>
    private int RouteBucketOnce(
        List<WaveguideConnection> order,
        WaveguideRouter router,
        IReadOnlyList<WaveguideConnection> routedBefore,
        Action? progressCallback,
        CancellationToken cancellationToken)
    {
        var grid = router.PathfindingGrid!;
        foreach (var connection in order)
            grid.RemoveWaveguideObstacle(connection.Id);

        int failedCount = 0;
        var routedSoFar = new List<WaveguideConnection>(routedBefore);
        foreach (var connection in order)
        {
            if (cancellationToken.IsCancellationRequested)
                return failedCount;

            connection.RecalculateTransmission(_router, cancellationToken: cancellationToken);
            RefreshStyledObstacleCollision(connection, router);
            progressCallback?.Invoke();

            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(
                    connection.Id,
                    connection.RoutedPath.Segments,
                    ObstacleWidthFor(connection));

                if (connection.IsBlockedFallback || CrossesAnyRoutedSibling(connection, routedSoFar))
                    failedCount++;
                routedSoFar.Add(connection);
            }
            else
            {
                failedCount++;
            }
        }

        return failedCount;
    }
}
