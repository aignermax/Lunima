using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP_Core.Analysis;

/// <summary>
/// Detects overlapping waveguide paths in a design, including regular connections
/// that cross frozen paths inside ComponentGroups.
/// </summary>
public class WaveguideOverlapDetector
{
    /// <summary>
    /// Half-width of a standard waveguide in micrometers (used for AABB padding on bends).
    /// </summary>
    private const double WaveguideHalfWidthMicrometers = 1.0;

    /// <summary>
    /// Describes any routed path (connection or frozen) for overlap checking.
    /// </summary>
    private record PathDescriptor(
        List<PathSegment> Segments,
        string Label,
        WaveguideConnection? Connection,
        double MidX,
        double MidY);

    /// <summary>
    /// Detects all overlapping waveguide path pairs and returns a design issue for each.
    /// Checks regular connections against frozen paths and frozen paths against each other.
    /// </summary>
    /// <param name="connections">Regular waveguide connections in the design.</param>
    /// <param name="groups">ComponentGroups whose frozen internal paths are included.</param>
    /// <returns>A list of overlap design issues, empty if none found.</returns>
    public List<DesignIssue> DetectOverlaps(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(groups);

        var paths = CollectPaths(connections, groups);
        return CheckAllPairs(paths);
    }

    /// <summary>
    /// Collects path descriptors from all connections and frozen group paths.
    /// </summary>
    private static List<PathDescriptor> CollectPaths(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups)
    {
        var paths = new List<PathDescriptor>();

        foreach (var conn in connections)
        {
            if (conn.RoutedPath?.Segments is { Count: > 0 } segs)
            {
                var label = FormatConnectionLabel(conn);
                var (midX, midY) = GetSegmentsMid(segs);
                paths.Add(new PathDescriptor(segs, label, conn, midX, midY));
            }
        }

        foreach (var group in groups)
        {
            foreach (var frozen in group.InternalPaths)
            {
                if (frozen.Path?.Segments is { Count: > 0 } segs)
                {
                    var label = $"Group '{group.Identifier}' frozen path";
                    var (midX, midY) = GetSegmentsMid(segs);
                    paths.Add(new PathDescriptor(segs, label, null, midX, midY));
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Checks every pair of paths for overlap, returning one issue per overlapping pair.
    /// </summary>
    private static List<DesignIssue> CheckAllPairs(List<PathDescriptor> paths)
    {
        var issues = new List<DesignIssue>();

        for (int i = 0; i < paths.Count; i++)
        {
            for (int j = i + 1; j < paths.Count; j++)
            {
                var overlap = FindFirstOverlapPoint(paths[i].Segments, paths[j].Segments);
                if (overlap.HasValue)
                {
                    issues.Add(CreateOverlapIssue(paths[i], paths[j], overlap.Value));
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Finds the first intersection point between two sets of path segments.
    /// Returns null if no overlap is found.
    /// </summary>
    private static (double X, double Y)? FindFirstOverlapPoint(
        List<PathSegment> segmentsA,
        List<PathSegment> segmentsB)
    {
        foreach (var segA in segmentsA)
        {
            foreach (var segB in segmentsB)
            {
                var point = CheckSegmentPairOverlap(segA, segB);
                if (point.HasValue)
                    return point;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks a pair of segments for overlap.
    /// Straight–straight uses exact intersection.
    /// Bend–straight uses precise arc sampling to avoid false positives from AABB.
    /// Bend–bend falls back to AABB (acceptable approximation).
    /// </summary>
    private static (double X, double Y)? CheckSegmentPairOverlap(PathSegment a, PathSegment b)
    {
        if (a is StraightSegment sa && b is StraightSegment sb)
            return StraightStraightIntersection(sa, sb);

        if (a is BendSegment bendA && b is StraightSegment straightB)
            return ArcStraightIntersection(bendA, straightB);

        if (a is StraightSegment straightA && b is BendSegment bendB)
            return ArcStraightIntersection(bendB, straightA);

        if (SegmentBoundsOverlap(a, b))
            return ((a.StartPoint.X + b.StartPoint.X) / 2.0,
                    (a.StartPoint.Y + b.StartPoint.Y) / 2.0);

        return null;
    }

    /// <summary>
    /// Checks if a bend arc intersects a straight segment using arc sampling.
    /// Samples the arc into small chords and tests each chord against the straight segment.
    /// This avoids false positives that occur with the coarser AABB approach.
    /// </summary>
    private static (double X, double Y)? ArcStraightIntersection(BendSegment bend, StraightSegment straight)
    {
        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        double sign = Math.Sign(bend.SweepAngleDegrees);
        if (sign == 0) sign = 1;

        int numSamples = Math.Max(20, (int)(Math.Abs(bend.SweepAngleDegrees) / 3));

        double prevX = 0, prevY = 0;
        for (int i = 0; i <= numSamples; i++)
        {
            double t = (double)i / numSamples;
            double angle = startRad + sweepRad * t;
            double px = bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle - Math.PI / 2 * sign);
            double py = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle - Math.PI / 2 * sign);

            if (i > 0)
            {
                var chord = new StraightSegment(prevX, prevY, px, py, angleDegrees: 0);
                var intersection = StraightStraightIntersection(chord, straight);
                if (intersection.HasValue)
                    return intersection;
            }

            prevX = px;
            prevY = py;
        }

        return null;
    }

    /// <summary>
    /// Exact line-segment intersection using the cross-product parametric method.
    /// Returns the intersection point if the two segments cross, null otherwise.
    /// </summary>
    private static (double X, double Y)? StraightStraightIntersection(
        StraightSegment a, StraightSegment b)
    {
        double ax = a.EndPoint.X - a.StartPoint.X;
        double ay = a.EndPoint.Y - a.StartPoint.Y;
        double bx = b.EndPoint.X - b.StartPoint.X;
        double by = b.EndPoint.Y - b.StartPoint.Y;

        double denom = ax * by - ay * bx;
        if (Math.Abs(denom) < 1e-10)
            return null; // Parallel or collinear

        double dx = b.StartPoint.X - a.StartPoint.X;
        double dy = b.StartPoint.Y - a.StartPoint.Y;

        double t = (dx * by - dy * bx) / denom;
        double u = (dx * ay - dy * ax) / denom;

        if (t < 0.0 || t > 1.0 || u < 0.0 || u > 1.0)
            return null; // Intersection outside segment extents

        return (a.StartPoint.X + t * ax, a.StartPoint.Y + t * ay);
    }

    /// <summary>
    /// Returns true if the axis-aligned bounding boxes of two segments overlap.
    /// Bend segments use the full circle bounding box with waveguide padding.
    /// </summary>
    private static bool SegmentBoundsOverlap(PathSegment a, PathSegment b)
    {
        var (ax1, ay1, ax2, ay2) = GetSegmentBounds(a);
        var (bx1, by1, bx2, by2) = GetSegmentBounds(b);

        return ax1 <= bx2 && ax2 >= bx1 && ay1 <= by2 && ay2 >= by1;
    }

    /// <summary>
    /// Returns (minX, minY, maxX, maxY) for a segment, padded by waveguide half-width.
    /// </summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) GetSegmentBounds(PathSegment seg)
    {
        double pad = WaveguideHalfWidthMicrometers;

        if (seg is BendSegment bend)
        {
            return (
                bend.Center.X - bend.RadiusMicrometers - pad,
                bend.Center.Y - bend.RadiusMicrometers - pad,
                bend.Center.X + bend.RadiusMicrometers + pad,
                bend.Center.Y + bend.RadiusMicrometers + pad);
        }

        return (
            Math.Min(seg.StartPoint.X, seg.EndPoint.X) - pad,
            Math.Min(seg.StartPoint.Y, seg.EndPoint.Y) - pad,
            Math.Max(seg.StartPoint.X, seg.EndPoint.X) + pad,
            Math.Max(seg.StartPoint.Y, seg.EndPoint.Y) + pad);
    }

    /// <summary>
    /// Creates a design issue for an overlapping path pair.
    /// Uses whichever connection is available for canvas highlighting.
    /// </summary>
    private static DesignIssue CreateOverlapIssue(
        PathDescriptor a, PathDescriptor b, (double X, double Y) point)
    {
        var connection = a.Connection ?? b.Connection;
        var description = $"Overlapping paths: {a.Label} ↔ {b.Label}";
        return new DesignIssue(DesignIssueType.OverlappingPaths, connection, point.X, point.Y, description);
    }

    /// <summary>
    /// Formats a connection label as "ComponentA.Pin → ComponentB.Pin".
    /// </summary>
    private static string FormatConnectionLabel(WaveguideConnection conn)
    {
        var start = $"{conn.StartPin.ParentComponent.Identifier}.{conn.StartPin.Name}";
        var end = $"{conn.EndPin.ParentComponent.Identifier}.{conn.EndPin.Name}";
        return $"{start} → {end}";
    }

    /// <summary>
    /// Returns the midpoint of the first segment as the representative path location.
    /// </summary>
    private static (double X, double Y) GetSegmentsMid(List<PathSegment> segments)
    {
        var first = segments[0];
        return ((first.StartPoint.X + first.EndPoint.X) / 2.0,
                (first.StartPoint.Y + first.EndPoint.Y) / 2.0);
    }
}
