using CAP_Core.Components;

namespace CAP_Core.Routing;

/// <summary>
/// Orchestrates sequential waveguide routing with collision avoidance.
/// Handles incremental routing, ordering strategies, and route validation.
/// </summary>
public class RoutingOrchestrator
{
    private const double EndpointToleranceMicrometers = 1.0;

    /// <summary>
    /// Maximum number of ordering permutations to try when routing fails.
    /// </summary>
    public int MaxRoutingAttempts { get; set; } = 3;

    /// <summary>
    /// Waveguide width for collision detection (in micrometers).
    /// </summary>
    public double WaveguideWidthMicrometers { get; set; } = 4.0;

    /// <summary>
    /// Routes only connections affected by a moved component.
    /// Unaffected connections are preserved as obstacles.
    /// Falls back to full RouteAll if affected routes fail.
    /// </summary>
    public void RouteAffected(
        List<WaveguideConnection> connections,
        Component movedComponent,
        CancellationToken ct = default)
    {
        var router = WaveguideConnection.SharedRouter;
        if (router.PathfindingGrid == null)
        {
            foreach (var conn in connections)
            {
                if (ct.IsCancellationRequested) return;
                if (conn.StartPin.ParentComponent == movedComponent ||
                    conn.EndPin.ParentComponent == movedComponent)
                    conn.RecalculateTransmission();
            }
            return;
        }

        var grid = router.PathfindingGrid;
        grid.ClearAllWaveguideObstacles();

        // Partition: affected (touch moved component) vs unaffected
        var affected = new List<WaveguideConnection>();
        var unaffected = new List<WaveguideConnection>();
        foreach (var conn in connections)
        {
            if (conn.StartPin.ParentComponent == movedComponent ||
                conn.EndPin.ParentComponent == movedComponent)
                affected.Add(conn);
            else
                unaffected.Add(conn);
        }

        // Re-add unaffected routes as obstacles if still valid
        foreach (var conn in unaffected)
        {
            if (ct.IsCancellationRequested) return;

            if (IsRouteStillValid(conn, router))
            {
                grid.AddWaveguideObstacle(
                    conn.Id, conn.RoutedPath!.Segments, WaveguideWidthMicrometers);
            }
            else
            {
                // Unaffected route became invalid (component moved onto it)
                affected.Add(conn);
            }
        }

        // Route only the affected connections
        int failedCount = 0;
        foreach (var conn in affected)
        {
            if (ct.IsCancellationRequested) return;

            conn.RecalculateTransmission();

            if (conn.IsPathValid && conn.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(
                    conn.Id, conn.RoutedPath.Segments, WaveguideWidthMicrometers);
            }
            else
            {
                failedCount++;
            }
        }

        // If some affected routes failed, fall back to full re-route
        if (failedCount > 0 && !ct.IsCancellationRequested)
            RouteAll(connections, ct);
    }

    /// <summary>
    /// Routes all connections using incremental routing with fallback to full re-route.
    /// </summary>
    public void RouteAll(List<WaveguideConnection> connections, CancellationToken ct = default)
    {
        var router = WaveguideConnection.SharedRouter;

        if (router.PathfindingGrid == null)
        {
            foreach (var connection in connections)
            {
                if (ct.IsCancellationRequested) return;
                connection.RecalculateTransmission();
            }
            return;
        }

        // Phase 1: Incremental — keep valid routes, only re-route broken ones
        var result = TryRouteIncremental(connections, router, ct);
        if (ct.IsCancellationRequested || result.allValid) return;

        // Phase 2: Full re-route with ordering strategies
        result = TryRouteInOrder(connections.ToList(), router, ct);
        if (ct.IsCancellationRequested || result.allValid) return;

        // Skip Phase 3 ordering attempts if only 1 connection failed —
        // reordering won't help a single failed route.
        if (result.failedCount <= 1) return;

        var bestOrder = connections.ToList();
        int bestFailedCount = result.failedCount;

        var orderings = GenerateOrderings(connections.ToList(), MaxRoutingAttempts - 1);
        foreach (var ordering in orderings)
        {
            if (ct.IsCancellationRequested) return;
            result = TryRouteInOrder(ordering, router, ct);

            if (result.allValid)
            {
                ReorderConnections(connections, ordering);
                return;
            }

            if (result.failedCount < bestFailedCount)
            {
                bestFailedCount = result.failedCount;
                bestOrder = ordering;
            }
        }

        if (!ct.IsCancellationRequested && bestOrder != connections.ToList())
        {
            ReorderConnections(connections, bestOrder);
            TryRouteInOrder(bestOrder, router, ct);
        }
    }

    private (bool allValid, int failedCount) TryRouteIncremental(
        List<WaveguideConnection> connections,
        WaveguideRouter router,
        CancellationToken ct)
    {
        var grid = router.PathfindingGrid!;
        grid.ClearAllWaveguideObstacles();

        var validConnections = new List<WaveguideConnection>();
        var invalidConnections = new List<WaveguideConnection>();

        foreach (var connection in connections)
        {
            if (ct.IsCancellationRequested) return (false, 0);

            if (IsRouteStillValid(connection, router))
                validConnections.Add(connection);
            else
                invalidConnections.Add(connection);
        }

        foreach (var connection in validConnections)
        {
            grid.AddWaveguideObstacle(
                connection.Id, connection.RoutedPath!.Segments, WaveguideWidthMicrometers);
        }

        int failedCount = 0;
        foreach (var connection in invalidConnections)
        {
            if (ct.IsCancellationRequested) return (false, failedCount);

            connection.RecalculateTransmission();

            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(
                    connection.Id, connection.RoutedPath.Segments, WaveguideWidthMicrometers);
            }
            else
            {
                failedCount++;
            }
        }

        return (failedCount == 0, failedCount);
    }

    private (bool allValid, int failedCount) TryRouteInOrder(
        List<WaveguideConnection> orderedConnections,
        WaveguideRouter router,
        CancellationToken ct)
    {
        router.PathfindingGrid!.ClearAllWaveguideObstacles();

        int failedCount = 0;
        foreach (var connection in orderedConnections)
        {
            if (ct.IsCancellationRequested) return (false, failedCount);

            connection.RecalculateTransmission();

            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                router.PathfindingGrid.AddWaveguideObstacle(
                    connection.Id, connection.RoutedPath.Segments, WaveguideWidthMicrometers);
            }
            else
            {
                failedCount++;
            }
        }

        return (failedCount == 0, failedCount);
    }

    private static bool IsRouteStillValid(WaveguideConnection connection, WaveguideRouter router)
    {
        if (connection.RoutedPath == null || !connection.IsPathValid)
            return false;
        if (connection.RoutedPath.IsBlockedFallback || connection.RoutedPath.Segments.Count == 0)
            return false;

        var (startX, startY) = connection.StartPin.GetAbsolutePosition();
        var (endX, endY) = connection.EndPin.GetAbsolutePosition();
        var firstSeg = connection.RoutedPath.Segments[0];
        var lastSeg = connection.RoutedPath.Segments[^1];

        double startDist = Distance(firstSeg.StartPoint.X, firstSeg.StartPoint.Y, startX, startY);
        double endDist = Distance(lastSeg.EndPoint.X, lastSeg.EndPoint.Y, endX, endY);

        if (startDist > EndpointToleranceMicrometers || endDist > EndpointToleranceMicrometers)
            return false;

        return !router.IsPathBlocked(connection.RoutedPath.Segments);
    }

    private static List<List<WaveguideConnection>> GenerateOrderings(
        List<WaveguideConnection> connections, int maxOrderings)
    {
        var orderings = new List<List<WaveguideConnection>>();
        if (connections.Count <= 1) return orderings;

        var reversed = connections.ToList();
        reversed.Reverse();
        orderings.Add(reversed);

        orderings.Add(connections.OrderByDescending(c => EstimateDistance(c)).ToList());
        orderings.Add(connections.OrderBy(c => EstimateDistance(c)).ToList());

        if (connections.Count >= 3 && orderings.Count < maxOrderings)
            orderings.Add(connections.OrderBy(_ => new Random(42).Next()).ToList());

        if (connections.Count >= 3 && orderings.Count < maxOrderings)
            orderings.Add(connections.OrderBy(_ => new Random(123).Next()).ToList());

        return orderings
            .Where(o => !o.SequenceEqual(connections))
            .Distinct(new ListComparer<WaveguideConnection>())
            .Take(maxOrderings)
            .ToList();
    }

    private static double EstimateDistance(WaveguideConnection c)
    {
        var (x1, y1) = c.StartPin.GetAbsolutePosition();
        var (x2, y2) = c.EndPin.GetAbsolutePosition();
        return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    }

    private static void ReorderConnections(
        List<WaveguideConnection> connections, List<WaveguideConnection> newOrder)
    {
        connections.Clear();
        connections.AddRange(newOrder);
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private class ListComparer<T> : IEqualityComparer<List<T>>
    {
        public bool Equals(List<T>? x, List<T>? y)
        {
            if (x == null || y == null) return x == y;
            return x.SequenceEqual(y);
        }

        public int GetHashCode(List<T> obj)
        {
            return obj.Aggregate(0, (hash, item) => hash ^ (item?.GetHashCode() ?? 0));
        }
    }
}
