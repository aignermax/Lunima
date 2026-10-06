namespace CAP_Core.Routing.ImportedGeometry;

/// <summary>
/// Assembles fitted waveguide pieces (<see cref="RibbonFit"/>) into one
/// <see cref="RoutedPath"/> running from a start pin to an end pin. Pieces come in
/// file order with arbitrary orientation; the chainer walks from the start pin,
/// always taking the piece whose nearer end is closest, reverses it when needed,
/// and closes the small joint gaps layout tools leave between pieces (tens of nm)
/// with short straights so the path is continuous.
/// </summary>
public static class FrozenRouteChainer
{
    /// <summary>
    /// Largest joint gap (µm) the chainer bridges. Real joints sit within a few
    /// tens of nm; a larger gap means the pieces do not form one route.
    /// </summary>
    public const double DefaultMaxGapUm = 1.0;

    /// <summary>
    /// Gaps at or below this (µm) are left as-is instead of getting a bridge straight —
    /// the continuity tolerance <see cref="RoutedPath.IsValid"/> accepts. A bridge across
    /// a few tens of nanometres would be a micro-segment pointing in an arbitrary
    /// direction, which reads as the route's launch angle at a pin.
    /// </summary>
    private const double NegligibleGapUm = 0.1;

    /// <summary>
    /// Builds the route, or returns null when the pieces cannot be chained into one
    /// path between the two pins (empty input, or a joint gap above
    /// <paramref name="maxGapUm"/>).
    /// </summary>
    /// <param name="pieces">Fitted pieces of one route, any order and orientation.</param>
    /// <param name="start">Start pin position (µm).</param>
    /// <param name="end">End pin position (µm).</param>
    /// <param name="maxGapUm">Largest gap bridged with a straight.</param>
    public static RoutedPath? Chain(
        IReadOnlyList<RibbonFit> pieces,
        (double X, double Y) start,
        (double X, double Y) end,
        double maxGapUm = DefaultMaxGapUm)
    {
        if (pieces.Count == 0) return null;
        var remaining = pieces.ToList();
        var path = new RoutedPath();
        var cursor = start;
        while (remaining.Count > 0)
        {
            var (index, reversed, gap) = Nearest(remaining, cursor);
            if (gap > maxGapUm) return null;
            var piece = reversed ? remaining[index].Reversed() : remaining[index];
            remaining.RemoveAt(index);
            AddBridge(path, cursor, piece.Start);
            path.Segments.AddRange(piece.Segments);
            cursor = piece.End;
        }

        if (RibbonSides.Distance(cursor, end) > maxGapUm) return null;
        AddBridge(path, cursor, end);
        return path;
    }

    private static (int Index, bool Reversed, double Gap) Nearest(List<RibbonFit> pieces, (double X, double Y) cursor)
    {
        var best = (Index: -1, Reversed: false, Gap: double.MaxValue);
        for (int i = 0; i < pieces.Count; i++)
        {
            double toStart = RibbonSides.Distance(cursor, pieces[i].Start);
            double toEnd = RibbonSides.Distance(cursor, pieces[i].End);
            if (toStart < best.Gap) best = (i, false, toStart);
            if (toEnd < best.Gap) best = (i, true, toEnd);
        }
        return best;
    }

    private static void AddBridge(RoutedPath path, (double X, double Y) from, (double X, double Y) to)
    {
        if (RibbonSides.Distance(from, to) <= NegligibleGapUm) return;
        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI;
        path.Segments.Add(new StraightSegment(from.X, from.Y, to.X, to.Y, PathSegmentReversal.NormalizeDegrees(angle)));
    }
}
