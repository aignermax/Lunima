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
    /// Recalculates transmission for all connections.
    /// When UseSequentialRouting is enabled, routes connections sequentially
    /// to avoid waveguide collisions.
    /// </summary>
    public void RecalculateAllTransmissions()
    {
        var router = WaveguideConnection.SharedRouter;

        if (UseSequentialRouting && router.PathfindingGrid != null)
        {
            // Clear all waveguide obstacles from previous routing
            router.PathfindingGrid.ClearAllWaveguideObstacles();

            // Route each connection sequentially
            foreach (var connection in Connections)
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

            // Light flows from StartPin's LogicalPin OutFlow to EndPin's LogicalPin InFlow
            // This maps the physical waveguide connection to the S-Matrix port IDs
            var startPinOutFlow = conn.StartPin.LogicalPin.IDOutFlow;
            var endPinInFlow = conn.EndPin.LogicalPin.IDInFlow;
            transfers[(startPinOutFlow, endPinInFlow)] = conn.TransmissionCoefficient;
        }
        return transfers;
    }
}
