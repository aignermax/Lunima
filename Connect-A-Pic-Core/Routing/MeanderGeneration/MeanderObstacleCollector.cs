using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// Gathers what a connection's meander must keep clear of from the live design: the routes of
/// every other connection and the footprints of all routing-obstacle components except the
/// ones the connection itself attaches to.
/// </summary>
public static class MeanderObstacleCollector
{
    /// <summary>Slack (µm) when deciding whether a pin sits on a footprint (pins lie on component edges).</summary>
    private const double PinOnFootprintToleranceMicrometers = 1e-3;

    /// <summary>Collects the obstacles for <paramref name="connection"/>'s meander.</summary>
    /// <param name="connection">The connection being length matched.</param>
    /// <param name="siblings">All connections of the design; the connection itself is skipped.</param>
    /// <param name="components">The design's placed components.</param>
    /// <param name="minSpacingMicrometers">Required centerline spacing to other routes.</param>
    public static MeanderObstacles Collect(
        WaveguideConnection connection,
        IEnumerable<WaveguideConnection> siblings,
        IReadOnlyList<Component> components,
        double minSpacingMicrometers)
    {
        var routes = siblings
            .Where(other => !ReferenceEquals(other, connection) && !SharesPin(connection, other))
            .Where(other => other.RoutedPath is { Segments.Count: > 0 } && !other.IsBlockedFallback)
            .Select(other => other.RoutedPath!)
            .ToList();
        var footprints = components
            .Where(component => component.IsRoutingObstacle)
            .Select(Footprint)
            .Where(footprint => !ContainsPin(footprint, connection.StartPin) && !ContainsPin(footprint, connection.EndPin))
            .ToList();
        return new MeanderObstacles(routes, footprints, minSpacingMicrometers,
            PhotonicConstants.StandardWaveguideWidthMicrometers);
    }

    private static MeanderBounds Footprint(Component component) => new(
        component.PhysicalX, component.PhysicalY,
        component.PhysicalX + component.WidthMicrometers, component.PhysicalY + component.HeightMicrometers);

    private static bool ContainsPin(MeanderBounds footprint, PhysicalPin? pin)
    {
        if (pin == null)
            return false;
        var (x, y) = pin.GetAbsolutePosition();
        return x >= footprint.MinX - PinOnFootprintToleranceMicrometers && x <= footprint.MaxX + PinOnFootprintToleranceMicrometers
            && y >= footprint.MinY - PinOnFootprintToleranceMicrometers && y <= footprint.MaxY + PinOnFootprintToleranceMicrometers;
    }

    private static bool SharesPin(WaveguideConnection a, WaveguideConnection b) =>
        ReferenceEquals(a.StartPin, b.StartPin) || ReferenceEquals(a.StartPin, b.EndPin)
        || ReferenceEquals(a.EndPin, b.StartPin) || ReferenceEquals(a.EndPin, b.EndPin);
}
