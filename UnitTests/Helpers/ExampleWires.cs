using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion;

namespace UnitTests.Helpers;

/// <summary>
/// Wire counting for example designs that route some wires through placed crossings: a wire
/// through a crossing is split into two docked pieces, so every crossing adds exactly two
/// connections (one per wire it carries) without adding a logical wire.
/// </summary>
public static class ExampleWires
{
    /// <summary>Connections per crossing beyond the logical wires: each of its two wires is split once.</summary>
    private const int ExtraConnectionsPerCrossing = 2;

    /// <summary>The design's logical wires: its connections with every crossing split undone.</summary>
    public static int LogicalWireCount(DesignCanvasViewModel canvas) =>
        canvas.Connections.Count - ExtraConnectionsPerCrossing * CrossingCount(canvas);

    /// <summary>Placed crossing components on the top level of the canvas.</summary>
    public static int CrossingCount(DesignCanvasViewModel canvas) =>
        canvas.Components.Count(c => CrossingComponentCatalog.IsCrossing(c.Component));

    /// <summary>
    /// The two ends of every logical wire: pieces docked onto crossings are followed straight
    /// through each crossing to the wire's far end. Each wire is reported once, from the
    /// piece that starts away from any crossing.
    /// </summary>
    public static IEnumerable<(PhysicalPin Start, PhysicalPin End)> LogicalWireEnds(IReadOnlyCollection<WaveguideConnection> connections)
    {
        var byPin = new Dictionary<PhysicalPin, WaveguideConnection>();
        foreach (var connection in connections)
        {
            byPin[connection.StartPin] = connection;
            byPin[connection.EndPin] = connection;
        }
        foreach (var connection in connections.Where(c => !CrossingComponentCatalog.IsCrossing(c.StartPin.ParentComponent)))
            yield return (connection.StartPin, FarEnd(connection, connection.EndPin, byPin));
    }

    private static PhysicalPin FarEnd(WaveguideConnection piece, PhysicalPin end, Dictionary<PhysicalPin, WaveguideConnection> byPin)
    {
        var visited = new HashSet<WaveguideConnection> { piece };
        while (CrossingComponentCatalog.StraightThroughExit(end) is { } exit
               && byPin.TryGetValue(exit, out var next) && visited.Add(next))
            end = ReferenceEquals(next.StartPin, exit) ? next.EndPin : next.StartPin;
        return end;
    }
}
