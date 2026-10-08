namespace CAP_Core.Routing;

/// <summary>
/// Geometric check of a route found with opened-up pin corridors: near a pin the grid clears
/// other waveguides' cells (the grid is coarser than dense pin pitches), so a route may pass
/// cells another wire occupies. Passing next to it is fine; cutting through it is not.
/// </summary>
public partial class WaveguideRouter
{
    /// <summary>Distance (µm) within which two route ends count as the same pin.</summary>
    private const double SharedPinToleranceMicrometers = 0.5;

    /// <summary>
    /// True when <paramref name="path"/> properly crosses one of the waveguides whose cells the
    /// attempt cleared — except wires sharing one of its pins and wires it crosses through a
    /// planned crossing.
    /// </summary>
    private bool CutsThroughClearedWaveguide(
        RoutedPath path, (double X, double Y) start, (double X, double Y) end,
        params Dictionary<(int x, int y), byte>[] clearedSets)
    {
        var crossedByPlan = LastPlannedCrossings.Select(c => c.CrossedConnection).ToHashSet();
        foreach (var (owner, other) in PathfindingGrid!.WaveguidesOwningClearedCells(clearedSets))
        {
            if (crossedByPlan.Contains(owner) || SharesPin(other, start, end)) continue;
            if (PathIntersectionDetector.Crosses(path, other)) return true;
        }
        return false;
    }

    private static bool SharesPin(RoutedPath other, (double X, double Y) start, (double X, double Y) end)
    {
        if (other.Segments.Count == 0) return false;
        var ends = new[] { other.Segments[0].StartPoint, other.Segments[^1].EndPoint };
        return ends.Any(e => Near(e, start) || Near(e, end));
    }

    private static bool Near((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) <= SharedPinToleranceMicrometers && Math.Abs(a.Y - b.Y) <= SharedPinToleranceMicrometers;
}
