using System.Numerics;
using CAP_Core.Routing;

namespace CAP_Core.Components;

public class WaveguideConnectionManager
{
    public List<WaveguideConnection> Connections { get; } = new();

    /// <summary>
    /// Default propagation loss applied to new connections (dB/cm).
    /// </summary>
    public double DefaultPropagationLossDbPerCm { get; set; } = 2.0;

    /// <summary>
    /// Default bend loss applied to new connections (dB per 90° bend).
    /// </summary>
    public double DefaultBendLossDbPer90Deg { get; set; } = 0.05;

    /// <summary>
    /// Whether to use sequential routing with waveguide collision avoidance.
    /// When enabled, already-routed waveguides are marked as obstacles for subsequent routes.
    /// </summary>
    public bool UseSequentialRouting { get; set; } = true;

    /// <summary>
    /// Waveguide width for collision detection (in micrometers).
    /// This is the waveguide core width plus minimum spacing on each side.
    /// Typical: 0.5µm core + 2µm clearance on each side = ~4.5µm total.
    /// </summary>
    public double WaveguideWidthMicrometers { get; set; } = 4.0;

    public WaveguideConnection AddConnection(PhysicalPin startPin, PhysicalPin endPin)
    {
        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = DefaultPropagationLossDbPerCm,
            BendLossDbPer90Deg = DefaultBendLossDbPer90Deg
        };
        Connections.Add(connection);

        // Recalculate ALL connections sequentially so the new one avoids existing waveguides
        // and existing ones are properly registered in the grid
        RecalculateAllTransmissions();

        return connection;
    }

    public void RemoveConnectionsForComponent(Component component)
    {
        var router = WaveguideConnection.SharedRouter;
        var connectionsToRemove = Connections
            .Where(c => c.StartPin.ParentComponent == component ||
                        c.EndPin.ParentComponent == component)
            .ToList();

        // Remove waveguide obstacles for removed connections
        if (router.PathfindingGrid != null)
        {
            foreach (var conn in connectionsToRemove)
            {
                router.PathfindingGrid.RemoveWaveguideObstacle(conn.Id);
            }
        }

        Connections.RemoveAll(c =>
            c.StartPin.ParentComponent == component ||
            c.EndPin.ParentComponent == component);

        // Recalculate remaining connections - they might find better routes now
        if (Connections.Count > 0)
        {
            RecalculateAllTransmissions();
        }
    }

    public void RemoveConnection(WaveguideConnection connection)
    {
        // Remove waveguide obstacle from pathfinding grid
        var router = WaveguideConnection.SharedRouter;
        if (router.PathfindingGrid != null)
        {
            router.PathfindingGrid.RemoveWaveguideObstacle(connection.Id);
        }

        Connections.Remove(connection);

        // Recalculate remaining connections - they might find better routes now
        if (Connections.Count > 0)
        {
            RecalculateAllTransmissions();
        }
    }

    public void AddExistingConnection(WaveguideConnection connection)
    {
        if (!Connections.Contains(connection))
        {
            Connections.Add(connection);
        }
    }

    public void Clear()
    {
        Connections.Clear();
    }

    /// <summary>
    /// Maximum number of ordering permutations to try when routing fails.
    /// </summary>
    public int MaxRoutingAttempts { get; set; } = 6;

    /// <summary>
    /// Recalculates transmission for all connections.
    /// When UseSequentialRouting is enabled, routes connections sequentially
    /// to avoid waveguide collisions. If routing fails, tries different orderings.
    /// </summary>
    public void RecalculateAllTransmissions()
    {
        var router = WaveguideConnection.SharedRouter;

        if (UseSequentialRouting && router.PathfindingGrid != null)
        {
            // Try the current ordering first
            var result = TryRouteInOrder(Connections.ToList(), router);

            // If all paths are valid, we're done
            if (result.allValid)
            {
                return;
            }

            // Some paths failed - try different orderings
            var bestOrder = Connections.ToList();
            int bestFailedCount = result.failedCount;

            // Generate different orderings to try
            var orderings = GenerateOrderings(Connections.ToList(), MaxRoutingAttempts - 1);

            foreach (var ordering in orderings)
            {
                result = TryRouteInOrder(ordering, router);

                if (result.allValid)
                {
                    // Found a perfect ordering - use it
                    ReorderConnections(ordering);
                    return;
                }

                if (result.failedCount < bestFailedCount)
                {
                    bestFailedCount = result.failedCount;
                    bestOrder = ordering;
                }
            }

            // Use the best ordering we found
            if (bestOrder != Connections.ToList())
            {
                ReorderConnections(bestOrder);
                TryRouteInOrder(bestOrder, router);
            }
        }
        else
        {
            // Simple routing without collision avoidance
            foreach (var connection in Connections)
            {
                connection.RecalculateTransmission();
            }
        }
    }

    /// <summary>
    /// Tries to route all connections in the given order.
    /// </summary>
    private (bool allValid, int failedCount) TryRouteInOrder(List<WaveguideConnection> orderedConnections, WaveguideRouter router)
    {
        // Clear all waveguide obstacles from previous routing
        router.PathfindingGrid!.ClearAllWaveguideObstacles();

        int failedCount = 0;

        // Route each connection sequentially
        foreach (var connection in orderedConnections)
        {
            connection.RecalculateTransmission();

            // If routing succeeded, mark this waveguide as an obstacle
            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                router.PathfindingGrid.AddWaveguideObstacle(
                    connection.Id,
                    connection.RoutedPath.Segments,
                    WaveguideWidthMicrometers);
            }
            else
            {
                failedCount++;
            }
        }

        return (failedCount == 0, failedCount);
    }

    /// <summary>
    /// Generates different orderings to try.
    /// Prioritizes connections by complexity (longer paths first, or paths involving blocked pins).
    /// </summary>
    private List<List<WaveguideConnection>> GenerateOrderings(List<WaveguideConnection> connections, int maxOrderings)
    {
        var orderings = new List<List<WaveguideConnection>>();

        if (connections.Count <= 1)
            return orderings;

        // Strategy 1: Reverse order
        var reversed = connections.ToList();
        reversed.Reverse();
        orderings.Add(reversed);

        // Strategy 2: Sort by estimated path length (longer first - they need more space)
        var byLength = connections.OrderByDescending(c =>
        {
            var (x1, y1) = c.StartPin.GetAbsolutePosition();
            var (x2, y2) = c.EndPin.GetAbsolutePosition();
            return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        }).ToList();
        orderings.Add(byLength);

        // Strategy 3: Sort by estimated path length (shorter first - they block less)
        var byLengthAsc = connections.OrderBy(c =>
        {
            var (x1, y1) = c.StartPin.GetAbsolutePosition();
            var (x2, y2) = c.EndPin.GetAbsolutePosition();
            return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        }).ToList();
        orderings.Add(byLengthAsc);

        // Strategy 4: Shuffle randomly (simple randomization)
        if (connections.Count >= 3 && orderings.Count < maxOrderings)
        {
            var random = new Random(42); // Fixed seed for reproducibility
            var shuffled = connections.OrderBy(_ => random.Next()).ToList();
            orderings.Add(shuffled);
        }

        // Strategy 5: Another random shuffle
        if (connections.Count >= 3 && orderings.Count < maxOrderings)
        {
            var random = new Random(123);
            var shuffled = connections.OrderBy(_ => random.Next()).ToList();
            orderings.Add(shuffled);
        }

        // Remove duplicates and limit to maxOrderings
        return orderings
            .Where(o => !o.SequenceEqual(connections)) // Remove if same as original
            .Distinct(new ListComparer<WaveguideConnection>())
            .Take(maxOrderings)
            .ToList();
    }

    /// <summary>
    /// Reorders the internal Connections list to match the given order.
    /// </summary>
    private void ReorderConnections(List<WaveguideConnection> newOrder)
    {
        Connections.Clear();
        Connections.AddRange(newOrder);
    }

    /// <summary>
    /// Helper class for comparing lists.
    /// </summary>
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

    /// <summary>
    /// Recalculates transmission for connections involving a specific component.
    /// When UseSequentialRouting is enabled, this triggers a full recalculation
    /// to ensure proper collision avoidance.
    /// </summary>
    public void RecalculateTransmissionsForComponent(Component component)
    {
        var router = WaveguideConnection.SharedRouter;

        if (UseSequentialRouting && router.PathfindingGrid != null)
        {
            // With sequential routing, we need to recalculate all connections
            // because moving one component might free up space for better routes
            RecalculateAllTransmissions();
        }
        else
        {
            // Simple routing: only recalculate affected connections
            foreach (var connection in Connections)
            {
                if (connection.StartPin.ParentComponent == component ||
                    connection.EndPin.ParentComponent == component)
                {
                    connection.RecalculateTransmission();
                }
            }
        }
    }

    /// <summary>
    /// Converts waveguide connections to S-Matrix compatible dictionary.
    /// Uses the LogicalPin IDOutFlow/IDInFlow for proper S-Matrix integration.
    /// Connections are bidirectional: light can flow in either direction through a waveguide.
    /// Physical pins without linked logical pins are skipped (they don't participate in light simulation).
    /// </summary>
    public Dictionary<(Guid PinIdInflow, Guid PinIdOutflow), Complex> GetConnectionTransfers()
    {
        var transfers = new Dictionary<(Guid, Guid), Complex>();
        foreach (var conn in Connections)
        {
            // Only include connections where both physical pins have linked logical pins
            if (conn.StartPin.LogicalPin == null || conn.EndPin.LogicalPin == null)
            {
                continue;
            }

            // Forward: light flows from StartPin OutFlow to EndPin InFlow
            var startPinOutFlow = conn.StartPin.LogicalPin.IDOutFlow;
            var endPinInFlow = conn.EndPin.LogicalPin.IDInFlow;
            transfers[(startPinOutFlow, endPinInFlow)] = conn.TransmissionCoefficient;

            // Reverse: light flows from EndPin OutFlow to StartPin InFlow
            // Waveguide connections are inherently bidirectional
            var endPinOutFlow = conn.EndPin.LogicalPin.IDOutFlow;
            var startPinInFlow = conn.StartPin.LogicalPin.IDInFlow;
            transfers[(endPinOutFlow, startPinInFlow)] = conn.TransmissionCoefficient;
        }
        return transfers;
    }
}
