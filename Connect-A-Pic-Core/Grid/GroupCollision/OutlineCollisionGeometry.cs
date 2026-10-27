using CAP_Core.Components.Core;
using System.Runtime.CompilerServices;
using System.Collections;

namespace CAP_Core.Grid.GroupCollision;

/// <summary>World-space imported geometry shared by placement and routing.</summary>
internal static class OutlineCollisionGeometry
{
    private static readonly ConditionalWeakTable<Component, Cache> Caches = new();

    // Outline lists are immutable and shared by templates and clones. The pose is mutable;
    // keep only its latest geometry so dragging does not accumulate snapshots.
    private sealed record Pose(IReadOnlyList<OutlinePolygon>? Outlines,
        double Width, double Height, double OriginalWidth, double OriginalHeight, double Angle, bool Mirrored);

    private sealed class Cache
    {
        public Pose? Pose;
        public PolygonBounds[] Polygons = Array.Empty<PolygonBounds>();
    }

    internal sealed record PolygonBounds(OutlinePoint[] Points, double MinX, double MinY, double MaxX, double MaxY);

    internal static PolygonBounds[] GetPolygons(Component component)
    {
        var pose = new Pose(component.OutlinePolygons,
            component.WidthMicrometers, component.HeightMicrometers,
            component.UnrotatedWidthMicrometers, component.UnrotatedHeightMicrometers,
            component.RotationDegrees, component.IsMirroredHorizontally);
        var cache = Caches.GetValue(component, _ => new Cache());
        lock (cache)
        {
            if (cache.Pose == pose) return cache.Polygons;
            cache.Polygons = TransformUncached(component).Select(points => new PolygonBounds(points,
                points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y))).ToArray();
            cache.Pose = pose;
            return cache.Polygons;
        }
    }

    public static IEnumerable<OutlinePoint[]> Transform(Component component) => GetPolygons(component)
        .Select(p => Shift(p.Points, component.PhysicalX, component.PhysicalY).ToArray());

    // Raster cache equality must inspect current point values even when a caller mutates
    // an existing outline array. The placement cache's reference-only pose is insufficient here.
    internal static IEnumerable<OutlinePoint[]> TransformFresh(Component component) => TransformUncached(component)
        .Select(points => Shift(points, component.PhysicalX, component.PhysicalY).ToArray());

    internal static IReadOnlyList<OutlinePoint> Shift(OutlinePoint[] points, double x, double y) =>
        x == 0 && y == 0 ? points : new ShiftedPoints(points, x, y);

    private sealed class ShiftedPoints(OutlinePoint[] points, double x, double y) : IReadOnlyList<OutlinePoint>
    {
        public int Count => points.Length;
        public OutlinePoint this[int index] => new(points[index].X + x, points[index].Y + y);
        public IEnumerator<OutlinePoint> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IEnumerable<OutlinePoint[]> TransformUncached(Component component)
    {
        double angle = component.RotationDegrees * Math.PI / 180;
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        bool swapped = Math.Abs(sin) > 0.999999;
        double width = component.UnrotatedWidthMicrometers > 0 ? component.UnrotatedWidthMicrometers
            : swapped ? component.HeightMicrometers : component.WidthMicrometers;
        double height = component.UnrotatedHeightMicrometers > 0 ? component.UnrotatedHeightMicrometers
            : swapped ? component.WidthMicrometers : component.HeightMicrometers;
        double cx = component.WidthMicrometers / 2;
        double cy = component.HeightMicrometers / 2;
        foreach (var outline in component.OutlinePolygons ?? Array.Empty<OutlinePolygon>())
        {
            if (outline.Points.Count < 3) continue;
            yield return outline.Points.Select(p =>
            {
                double x = p.X - width / 2;
                double y = (component.IsMirroredHorizontally ? height - p.Y : p.Y) - height / 2;
                return new OutlinePoint(cx + x * cos - y * sin, cy + x * sin + y * cos);
            }).ToArray();
        }
    }

    public static bool Contains(IReadOnlyList<OutlinePoint> polygon, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[j]; var b = polygon[i];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static double ToSegment(IReadOnlyList<OutlinePoint> polygon, double ax, double ay, double bx, double by)
    {
        if (Contains(polygon, ax, ay) || Contains(polygon, bx, by)) return 0;
        double distance = double.PositiveInfinity;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            distance = Math.Min(distance, CollisionDistance.SegmentToSegment(
                ax, ay, bx, by, polygon[j].X, polygon[j].Y, polygon[i].X, polygon[i].Y));
            if (distance == 0) return 0;
        }
        return distance;
    }

    public static double ToRectangle(IReadOnlyList<OutlinePoint> polygon, double x1, double y1, double x2, double y2)
    {
        if (Contains(polygon, x1, y1)) return 0;
        double distance = double.PositiveInfinity;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            distance = Math.Min(distance, CollisionDistance.SegmentToRect(
                polygon[j].X, polygon[j].Y, polygon[i].X, polygon[i].Y, x1, y1, x2, y2));
            if (distance == 0) return 0;
        }
        return distance;
    }

    public static bool WithinDistance(IReadOnlyList<OutlinePoint> a, IReadOnlyList<OutlinePoint> b,
        double clearance, Component owner)
    {
        if (Contains(a, b[0].X, b[0].Y) || Contains(b, a[0].X, a[0].Y)) return true;
        var edges = new List<CollisionPrimitive>(b.Count);
        for (int i = 0, j = b.Count - 1; i < b.Count; j = i++)
            edges.Add(CollisionPrimitive.Waveguide(b[j].X, b[j].Y, b[i].X, b[i].Y, 0, owner));
        double extent = Math.Max(edges.Max(e => e.MaxX) - edges.Min(e => e.MinX),
            edges.Max(e => e.MaxY) - edges.Min(e => e.MinY));
        var index = new CollisionSpatialIndex(edges, Math.Max(clearance, extent / Math.Sqrt(b.Count)));
        for (int i = 0, j = a.Count - 1; i < a.Count; j = i++)
        {
            var start = a[j]; var end = a[i];
            if (index.AnyNear(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                    Math.Max(start.X, end.X), Math.Max(start.Y, end.Y), clearance,
                    edge => CollisionDistance.SegmentToSegment(start.X, start.Y, end.X, end.Y,
                        edge.Ax, edge.Ay, edge.Bx, edge.By) < clearance)) return true;
        }
        return false;
    }
}
