using System.Numerics;

namespace CAP_Core.Components;

public class WaveguideConnectionManager
{
    public List<WaveguideConnection> Connections { get; } = new();

    public void AddConnection(PhysicalPin startPin, PhysicalPin endPin)
    {
        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin
        };
        Connections.Add(connection);
    }

    public void RemoveConnectionsForComponent(Component component)
    {
        Connections.RemoveAll(c =>
            c.StartPin.ParentComponent == component ||
            c.EndPin.ParentComponent == component);
    }

    // Für S-Matrix: Connections zu Dictionary konvertieren
    public Dictionary<(Guid PinIdInflow, Guid PinIdOutflow), Complex> GetConnectionTransfers()
    {
        var transfers = new Dictionary<(Guid, Guid), Complex>();
        foreach (var conn in Connections)
        {
            // Light flows from StartPin.Out to EndPin.In
            transfers[(conn.StartPin.PinId, conn.EndPin.PinId)] = conn.TransmissionCoefficient;
        }
        return transfers;
    }
}
