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

    /// <summary>
    /// Adds a connection with a pre-computed cached route, bypassing A* routing.
    /// Registers the cached route as an obstacle in the pathfinding grid.
    /// Used when loading designs with cached route data.
    /// </summary>
    public WaveguideConnection AddConnectionWithCachedRoute(
        PhysicalPin startPin,
        PhysicalPin endPin,
        RoutedPath cachedPath)
    {
        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = DefaultPropagationLossDbPerCm,
            BendLossDbPer90Deg = DefaultBendLossDbPer90Deg
        };

        connection.RestoreCachedPath(cachedPath);
        Connections.Add(connection);

        // Register cached route as obstacle for future routing
        var router = WaveguideConnection.SharedRouter;
        if (UseSequentialRouting && router.PathfindingGrid != null &&
            connection.IsPathValid && connection.RoutedPath != null)
        {
            router.PathfindingGrid.AddWaveguideObstacle(
                connection.Id,
                connection.RoutedPath.Segments,
                WaveguideWidthMicrometers);
        }

        return connection;
    }

    /// <summary>
    /// Adds a connection without triggering route calculation.
    /// Used for async routing: add connection first, then route asynchronously.
    /// </summary>
    public WaveguideConnection AddConnectionDeferred(PhysicalPin startPin, PhysicalPin endPin)
    {
        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = DefaultPropagationLossDbPerCm,
            BendLossDbPer90Deg = DefaultBendLossDbPer90Deg
        };
        Connections.Add(connection);
        return connection;
    }

    /// <summary>
    /// Asynchronously recalculates all transmissions on a background thread.
    /// Returns false if cancelled before completion. Never throws on cancellation.
    /// </summary>
    public async Task<bool> RecalculateAllTransmissionsAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;

        return await Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            RecalculateAllTransmissions(cancellationToken);
            return !cancellationToken.IsCancellationRequested;
        });
    }

    /// <summary>
    /// Removes all connections for a component without triggering route recalculation.
    /// Caller is responsible for triggering RecalculateAllTransmissionsAsync().
    /// </summary>
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

    /// <summary>
    /// Removes a connection without triggering route recalculation.
    /// Used for async routing: remove connection first, then route asynchronously.
    /// </summary>
    public void RemoveConnectionDeferred(WaveguideConnection connection)
    {
        var router = WaveguideConnection.SharedRouter;
        if (router.PathfindingGrid != null)
        {
            router.PathfindingGrid.RemoveWaveguideObstacle(connection.Id);
        }
        Connections.Remove(connection);
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

    private readonly RoutingOrchestrator _routingOrchestrator = new();

    /// <summary>
    /// Maximum number of ordering permutations to try when routing fails.
    /// </summary>
    public int MaxRoutingAttempts
    {
        get => _routingOrchestrator.MaxRoutingAttempts;
        set => _routingOrchestrator.MaxRoutingAttempts = value;
    }

    /// <summary>
    /// Recalculates transmission for all connections using incremental routing.
    /// Existing valid routes are preserved; only broken or new connections are re-routed.
    /// Falls back to full re-route if incremental routing leaves failed connections.
    /// </summary>
    public void RecalculateAllTransmissions(CancellationToken cancellationToken = default)
    {
        var router = WaveguideConnection.SharedRouter;

        if (UseSequentialRouting && router.PathfindingGrid != null)
        {
            _routingOrchestrator.WaveguideWidthMicrometers = WaveguideWidthMicrometers;
            _routingOrchestrator.RouteAll(Connections, cancellationToken);
        }
        else
        {
            foreach (var connection in Connections)
            {
                if (cancellationToken.IsCancellationRequested) return;
                connection.RecalculateTransmission();
            }
        }
    }

    /// <summary>
    /// Recalculates transmission for connections involving a specific component.
    /// With sequential routing, only affected connections are re-routed while
    /// unaffected ones are preserved as obstacles for collision avoidance.
    /// </summary>
    public void RecalculateTransmissionsForComponent(
        Component component, CancellationToken cancellationToken = default)
    {
        var router = WaveguideConnection.SharedRouter;

        if (UseSequentialRouting && router.PathfindingGrid != null)
        {
            _routingOrchestrator.WaveguideWidthMicrometers = WaveguideWidthMicrometers;
            _routingOrchestrator.RouteAffected(Connections, component, cancellationToken);
        }
        else
        {
            foreach (var connection in Connections)
            {
                if (cancellationToken.IsCancellationRequested) return;
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
