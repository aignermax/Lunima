using System.Numerics;

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

    public WaveguideConnection AddConnection(PhysicalPin startPin, PhysicalPin endPin)
    {
        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = DefaultPropagationLossDbPerCm,
            BendLossDbPer90Deg = DefaultBendLossDbPer90Deg
        };
        connection.RecalculateTransmission();
        Connections.Add(connection);
        return connection;
    }

    public void RemoveConnectionsForComponent(Component component)
    {
        Connections.RemoveAll(c =>
            c.StartPin.ParentComponent == component ||
            c.EndPin.ParentComponent == component);
    }

    public void RemoveConnection(WaveguideConnection connection)
    {
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

    /// <summary>
    /// Recalculates transmission for all connections.
    /// Call this after any component has been moved.
    /// </summary>
    public void RecalculateAllTransmissions()
    {
        foreach (var connection in Connections)
        {
            connection.RecalculateTransmission();
        }
    }

    /// <summary>
    /// Recalculates transmission for connections involving a specific component.
    /// Call this after a single component has been moved.
    /// </summary>
    public void RecalculateTransmissionsForComponent(Component component)
    {
        foreach (var connection in Connections)
        {
            if (connection.StartPin.ParentComponent == component ||
                connection.EndPin.ParentComponent == component)
            {
                connection.RecalculateTransmission();
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
