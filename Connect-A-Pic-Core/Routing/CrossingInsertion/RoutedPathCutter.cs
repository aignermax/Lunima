namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Cuts a routed path where a crossing component sits: the straight segment through the
/// crossing centre loses the stretch the component covers, leaving the part before (ending
/// at the entry port) and the part after (starting at the exit port). The geometry of both
/// parts is the route's own — nothing is re-routed.
/// </summary>
public static class RoutedPathCutter
{
    /// <summary>Largest distance (µm) of the centre from a segment's line for it to count as on it.</summary>
    private const double OnSegmentToleranceMicrometers = 0.5;

    /// <summary>Shortest straight (µm) kept as its own segment; anything shorter is a joint.</summary>
    private const double MinSegmentLengthMicrometers = 1e-6;

    /// <summary>
    /// Splits <paramref name="path"/> around <paramref name="center"/>, removing
    /// <paramref name="halfLength"/> on both sides along the straight segment through it.
    /// Returns false when no straight segment carries the centre with that much room.
    /// </summary>
    /// <param name="path">The routed path to cut (not modified).</param>
    /// <param name="center">The crossing centre (µm).</param>
    /// <param name="halfLength">Half the crossing's edge (µm).</param>
    /// <param name="before">Path from the start to the entry cut.</param>
    /// <param name="after">Path from the exit cut to the end.</param>
    /// <param name="direction">Unit travel direction through the crossing.</param>
    public static bool TryCutAround(RoutedPath path, (double X, double Y) center, double halfLength,
                                    out RoutedPath before, out RoutedPath after, out (double X, double Y) direction)
    {
        before = new RoutedPath();
        after = new RoutedPath();
        direction = default;
        for (int i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i] is not StraightSegment straight || !TryLocate(straight, center, halfLength, out var along, out direction))
                continue;
            var (sx, sy) = straight.StartPoint;
            var entry = (X: sx + direction.X * (along - halfLength), Y: sy + direction.Y * (along - halfLength));
            var exit = (X: sx + direction.X * (along + halfLength), Y: sy + direction.Y * (along + halfLength));
            before.Segments.AddRange(path.Segments.Take(i));
            AddStraight(before, straight.StartPoint, entry, straight.StartAngleDegrees);
            AddStraight(after, exit, straight.EndPoint, straight.StartAngleDegrees);
            after.Segments.AddRange(path.Segments.Skip(i + 1));
            return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="path"/> has a straight segment carrying <paramref name="center"/>.</summary>
    public static bool Carries(RoutedPath? path, (double X, double Y) center, double halfLength) =>
        path != null && path.Segments.OfType<StraightSegment>().Any(s => TryLocate(s, center, halfLength, out _, out _));

    /// <summary>
    /// The exact point where <paramref name="first"/> and <paramref name="second"/> cross at a
    /// right angle near <paramref name="near"/> — the planned crossing comes from the routing
    /// grid, quantized to cell centres, while the cut must sit on the routed geometry itself.
    /// Returns false when no pair of perpendicular, axis-aligned straight segments meets within
    /// <paramref name="searchRadius"/> of the point.
    /// </summary>
    /// <param name="first">One routed path.</param>
    /// <param name="second">The other routed path.</param>
    /// <param name="near">The approximate crossing point (µm).</param>
    /// <param name="searchRadius">How far (µm) the exact point may lie from <paramref name="near"/>.</param>
    /// <param name="center">The exact crossing point.</param>
    public static bool TryFindRightAngleCrossing(RoutedPath first, RoutedPath second, (double X, double Y) near,
                                                 double searchRadius, out (double X, double Y) center)
    {
        center = default;
        foreach (var a in AxisAlignedStraightsNear(first, near, searchRadius))
        foreach (var b in AxisAlignedStraightsNear(second, near, searchRadius))
        {
            if (a.Horizontal == b.Horizontal) continue;
            var (horizontal, vertical) = a.Horizontal ? (a.Segment, b.Segment) : (b.Segment, a.Segment);
            var point = (X: vertical.StartPoint.X, Y: horizontal.StartPoint.Y);
            if (Math.Abs(point.X - near.X) > searchRadius || Math.Abs(point.Y - near.Y) > searchRadius) continue;
            if (!Within(point.X, horizontal.StartPoint.X, horizontal.EndPoint.X) || !Within(point.Y, vertical.StartPoint.Y, vertical.EndPoint.Y))
                continue;
            center = point;
            return true;
        }
        return false;
    }

    private static IEnumerable<(StraightSegment Segment, bool Horizontal)> AxisAlignedStraightsNear(
        RoutedPath path, (double X, double Y) near, double radius)
    {
        foreach (var segment in path.Segments.OfType<StraightSegment>())
        {
            bool horizontal = Math.Abs(segment.StartPoint.Y - segment.EndPoint.Y) <= OnSegmentToleranceMicrometers;
            bool vertical = Math.Abs(segment.StartPoint.X - segment.EndPoint.X) <= OnSegmentToleranceMicrometers;
            if (horizontal == vertical) continue;
            double across = horizontal ? segment.StartPoint.Y - near.Y : segment.StartPoint.X - near.X;
            if (Math.Abs(across) <= radius)
                yield return (segment, horizontal);
        }
    }

    private static bool Within(double value, double a, double b) =>
        value >= Math.Min(a, b) - OnSegmentToleranceMicrometers && value <= Math.Max(a, b) + OnSegmentToleranceMicrometers;

    /// <summary>The centre's distance along <paramref name="segment"/>, when it lies on it with room on both sides.</summary>
    private static bool TryLocate(StraightSegment segment, (double X, double Y) center, double halfLength,
                                  out double along, out (double X, double Y) direction)
    {
        double dx = segment.EndPoint.X - segment.StartPoint.X, dy = segment.EndPoint.Y - segment.StartPoint.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        along = 0;
        direction = default;
        if (length <= 2 * halfLength) return false;
        direction = (dx / length, dy / length);
        double rx = center.X - segment.StartPoint.X, ry = center.Y - segment.StartPoint.Y;
        along = rx * direction.X + ry * direction.Y;
        double offset = Math.Abs(rx * -direction.Y + ry * direction.X);
        return offset <= OnSegmentToleranceMicrometers && along >= halfLength && along <= length - halfLength;
    }

    private static void AddStraight(RoutedPath path, (double X, double Y) from, (double X, double Y) to, double angle)
    {
        double length = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
        if (length > MinSegmentLengthMicrometers)
            path.Segments.Add(new StraightSegment(from.X, from.Y, to.X, to.Y, angle));
    }
}
