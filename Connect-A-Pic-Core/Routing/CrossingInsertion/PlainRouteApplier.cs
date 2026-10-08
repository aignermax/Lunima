using CAP_Core.Components.Connections;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// The crossing-aware search sometimes connects a blocked wire without any crossing — the
/// space the earlier pass lacked has opened up since (other wires moved into chains, the
/// fallback placeholders were lifted). Such a route is applied as an ordinary route, after
/// the same geometric check the chains get.
/// </summary>
internal static class PlainRouteApplier
{
    /// <summary>
    /// Gives <paramref name="blocked"/> the crossing-free <paramref name="route"/>. Returns false
    /// (and leaves the wire blocked) when the route cuts through another wire or does not
    /// yield a valid path.
    /// </summary>
    /// <param name="blocked">The blocked connection.</param>
    /// <param name="route">The crossing-free route the search found.</param>
    /// <param name="manager">The connection manager owning the design's connections.</param>
    /// <param name="router">The router whose grid gets the new obstacle.</param>
    public static bool TryApply(WaveguideConnection blocked, RoutedPath route,
                                WaveguideConnectionManager manager, WaveguideRouter router)
    {
        if (ChainPieceCrossingGuard.CrossesAnotherWire(new[] { (blocked, route) }, new[] { blocked }, manager))
            return false;
        var fallback = blocked.RoutedPath;
        lock (manager.SyncRoot)
        {
            blocked.RestoreCachedPath(route);
            if (blocked.IsPathValid && !blocked.IsBlockedFallback && blocked.RoutedPath != null)
            {
                router.PathfindingGrid!.AddWaveguideObstacle(blocked.Id, blocked.RoutedPath.Segments, manager.WaveguideWidthMicrometers);
                return true;
            }
            if (fallback != null)
                blocked.RestoreCachedPath(fallback);
            return false;
        }
    }
}
