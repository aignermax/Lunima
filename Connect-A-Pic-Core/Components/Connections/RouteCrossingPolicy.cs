using CAP_Core.Routing;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Decides when a crossing between freshly routed wires is a routing failure — a reason to send
/// the design through the ordering cascade, which may untangle it by routing in another order.
/// </summary>
internal static class RouteCrossingPolicy
{
    /// <summary>
    /// True when the connection's routed geometry properly crosses an already-routed sibling in
    /// this pass. Touching endpoints (shared fan-out regions) do not count, and neither does a
    /// crossing between two fixed-style routes (Bend, SBend, Cobra): that is their drawn shape,
    /// and no routing order changes it.
    /// </summary>
    public static bool CrossesAnyRoutedSibling(WaveguideConnection connection, List<WaveguideConnection> routedSoFar)
    {
        bool fixedShape = connection.Type != WaveguideType.Auto;
        return routedSoFar.Any(other =>
            other.RoutedPath != null &&
            !(fixedShape && other.Type != WaveguideType.Auto) &&
            PathIntersectionDetector.Crosses(connection.RoutedPath!, other.RoutedPath));
    }
}
