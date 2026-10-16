using CAP_Core.Routing;

namespace CAP_Core.Components.Connections;

/// <summary>Rejects cached generated arcs whose endpoint headings no longer match their pins.</summary>
internal static class GeneratedRoutePinDirections
{
    public static bool NeedsRepair(WaveguideConnection connection)
    {
        if (connection.HasManualPathEdits || connection.AsDrawnGeometry != null
            || connection.RoutedPath is not { } path
            || (!(connection.Type == WaveguideType.Bend && connection.IsRouteFrozen)
                && (!path.IsDirectStyledRoute || !path.Segments.Any(s => s is BendSegment))))
            return false;

        var (startMatches, endMatches) = CachedRouteValidator.CheckPinDirections(
            connection.StartPin, connection.EndPin, path);
        return !startMatches || !endMatches;
    }
}
