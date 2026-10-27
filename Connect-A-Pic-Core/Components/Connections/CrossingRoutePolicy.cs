using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Components.Connections;

/// <summary>Shared route-state policy for crossing-chain maintenance and collision/repair marking.</summary>
internal static class CrossingRoutePolicy
{
    internal const double EndpointToleranceMicrometers = 1.0;

    /// <summary>
    /// A piece of a routing-placed crossing whose route still ends at both pins: it stays as
    /// it is (no re-route, not part of the ordering cascade) until it goes stale, at which
    /// point its whole chain is taken out.
    /// </summary>
    internal static bool IsSettledPiece(WaveguideConnection connection) =>
        ChainCrossingMaintenance.DocksOnTemporaryCrossing(connection) && !IsStalePiece(connection);

    /// <summary>
    /// A piece whose route cannot stay: gone, blocked, or detached from one of its pins (a
    /// component it ends at moved). Grid collisions are deliberately not a criterion: a piece
    /// docks into a crossing port and often ends between the narrow pins of a gate input, so
    /// the rasterized grid reports settled pieces as touching component cells.
    /// </summary>
    internal static bool IsStalePiece(WaveguideConnection piece)
    {
        var path = piece.RoutedPath;
        if (path == null || !piece.IsPathValid || path.IsBlockedFallback || path.Segments.Count == 0)
            return true;
        var (startX, startY) = piece.StartPin.GetAbsolutePosition();
        var (endX, endY) = piece.EndPin.GetAbsolutePosition();
        return Distance(path.Segments[0].StartPoint.X, path.Segments[0].StartPoint.Y, startX, startY) > EndpointToleranceMicrometers
               || Distance(path.Segments[^1].EndPoint.X, path.Segments[^1].EndPoint.Y, endX, endY) > EndpointToleranceMicrometers;
    }

    /// <summary>
    /// Picks which side of a crossing pair to degrade: the LATER re-routable Auto route
    /// (it was routed with the earlier one already registered), the earlier one when only
    /// that is re-routable, or null when both shapes are forced.
    /// </summary>
    internal static WaveguideConnection? PickReroutableSide(
        WaveguideConnection first, WaveguideConnection second)
    {
        static bool IsReroutable(WaveguideConnection c) =>
            c.Type == WaveguideType.Auto && !c.IsRouteFrozen && !IsSettledPiece(c);

        if (IsReroutable(second)) return second;
        if (IsReroutable(first)) return first;
        return null;
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
