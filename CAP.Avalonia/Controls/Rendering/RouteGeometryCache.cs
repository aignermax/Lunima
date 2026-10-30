using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using BendSegment = CAP_Core.Routing.BendSegment;
using PathSegment = CAP_Core.Routing.PathSegment;
using RoutedPath = CAP_Core.Routing.RoutedPath;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// One drawable polyline geometry per route and zoom level, built once and reused every frame.
/// Drawing each arc as a run of separate lines cost tens of thousands of draw calls per frame on
/// a chip full of meanders; here a route is a single geometry. The level of detail follows the
/// zoom: points closer than about half a screen pixel are merged, so a zoomed-out meander is a
/// few dozen points instead of thousands. A route edited in place is noticed by its signature
/// (segment count, joints, bend radii) and rebuilt.
/// </summary>
internal static class RouteGeometryCache
{
    /// <summary>Screen pixels below which neighbouring polyline points are merged.</summary>
    private const double MergeScreenPx = 0.5;
    /// <summary>Arc sampling: at most this many degrees per chord at full detail.</summary>
    private const double MaxChordDegrees = 5.0;
    private const int MinArcChords = 8;
    private const double HashFactor = 1.000003;

    private sealed record Entry(int Level, Signature Key, Geometry Geometry);

    private readonly record struct Signature(int Count, double StartX, double StartY, double EndX, double EndY, double Hash);

    private static readonly ConditionalWeakTable<RoutedPath, Entry> Cache = new();

    /// <summary>The route's geometry for <paramref name="zoom"/>, from the cache when it is still current.</summary>
    public static Geometry For(RoutedPath path, double zoom)
    {
        int level = (int)Math.Floor(Math.Log2(Math.Max(zoom, 1e-6)));
        var key = SignatureOf(path.Segments);
        if (Cache.TryGetValue(path, out var entry) && entry.Level == level && entry.Key == key)
            return entry.Geometry;
        var geometry = Build(path.Segments, MergeScreenPx / Math.Pow(2, level));
        Cache.AddOrUpdate(path, new Entry(level, key, geometry));
        return geometry;
    }

    /// <summary>Builds an uncached geometry for loose segments (e.g. a route being dragged).</summary>
    public static Geometry Build(IReadOnlyList<PathSegment> segments, double mergeWorld)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        bool open = false;
        Point last = default;
        foreach (var segment in segments)
        {
            var start = new Point(segment.StartPoint.X, segment.StartPoint.Y);
            if (!open || Distance(last, start) > mergeWorld)
            {
                if (open)
                    context.EndFigure(false);
                context.BeginFigure(start, false);
                (open, last) = (true, start);
            }
            foreach (var point in Points(segment))
            {
                if (Distance(last, point) < mergeWorld && point != End(segment))
                    continue;
                context.LineTo(point);
                last = point;
            }
        }
        if (open)
            context.EndFigure(false);
        return geometry;
    }

    private static IEnumerable<Point> Points(PathSegment segment)
    {
        if (segment is not BendSegment bend || Math.Abs(bend.SweepAngleDegrees) < 0.1)
        {
            yield return End(segment);
            yield break;
        }
        int chords = Math.Max(MinArcChords, (int)Math.Ceiling(Math.Abs(bend.SweepAngleDegrees) / MaxChordDegrees));
        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        double sign = Math.Sign(bend.SweepAngleDegrees);
        for (int i = 1; i <= chords; i++)
        {
            double angle = startRad + sweepRad * i / chords - Math.PI / 2 * sign;
            yield return new Point(bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle),
                bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle));
        }
    }

    private static Point End(PathSegment segment) => new(segment.EndPoint.X, segment.EndPoint.Y);

    /// <summary>
    /// Cheap fingerprint of the route: segment count, end points and a running hash of every
    /// joint and bend radius — enough to notice an in-place edit without measuring lengths.
    /// </summary>
    private static Signature SignatureOf(IReadOnlyList<PathSegment> segments)
    {
        if (segments.Count == 0)
            return default;
        double hash = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var (x, y) = segments[i].EndPoint;
            double radius = segments[i] is BendSegment bend ? bend.RadiusMicrometers : 0;
            hash = hash * HashFactor + x * (i + 1) + y * (i + 2) + radius;
        }
        return new Signature(segments.Count, segments[0].StartPoint.X, segments[0].StartPoint.Y,
            segments[^1].EndPoint.X, segments[^1].EndPoint.Y, hash);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
