using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion.ManualCrossing;

/// <summary>
/// Gives the two halves of a Cut-tool split the cut wire's own geometry: the straight
/// segment under the new crossing loses the stretch between its entry and exit port, the rest
/// of the route stays exactly as it was. Nothing is re-routed, so a cut never disturbs the
/// wire — in particular a piece of a routing-placed crossing chain keeps ending at the chain's
/// crossing, and the chain is not taken out as stale.
/// </summary>
public static class ManualCrossingRouteSplitter
{
    /// <summary>Largest gap (µm) between a cut end and the crossing port it must dock onto.</summary>
    private const double PortToleranceMicrometers = 0.5;

    /// <summary>
    /// Cuts <paramref name="original"/>'s route around the crossing centred at
    /// <paramref name="center"/>. The crossing must already sit at its final position, so its
    /// <paramref name="entry"/> and <paramref name="exit"/> ports are where the halves end.
    /// Returns false (nothing produced) when the route has no straight carrying the centre with
    /// room for the crossing, or the cut ends miss the ports — the halves are then routed anew.
    /// </summary>
    /// <param name="original">The wire being split (its route is not modified).</param>
    /// <param name="center">The crossing centre (µm).</param>
    /// <param name="entry">The crossing port the first half ends at.</param>
    /// <param name="exit">The crossing port the second half starts from.</param>
    /// <param name="before">Route from the wire's start pin to <paramref name="entry"/>.</param>
    /// <param name="after">Route from <paramref name="exit"/> to the wire's end pin.</param>
    public static bool TrySplit(
        WaveguideConnection original, (double X, double Y) center, PhysicalPin entry, PhysicalPin exit,
        out RoutedPath before, out RoutedPath after)
    {
        before = after = new RoutedPath();
        if (original.RoutedPath is not { } path || path.Segments.Count == 0 || path.IsBlockedFallback)
            return false;

        var (entryX, entryY) = entry.GetAbsolutePosition();
        double halfLength = Math.Sqrt(Math.Pow(entryX - center.X, 2) + Math.Pow(entryY - center.Y, 2));
        if (!RoutedPathCutter.TryCutAround(path.DeepCopy(), center, halfLength, out before, out after, out _))
            return false;
        if (before.Segments.Count == 0 || after.Segments.Count == 0)
            return false;
        return Near(before.Segments[^1].EndPoint, entry) && Near(after.Segments[0].StartPoint, exit);
    }

    private static bool Near((double X, double Y) point, PhysicalPin pin)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return Math.Abs(point.X - x) <= PortToleranceMicrometers && Math.Abs(point.Y - y) <= PortToleranceMicrometers;
    }
}
