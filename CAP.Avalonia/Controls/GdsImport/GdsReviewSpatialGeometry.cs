using Avalonia;
using Avalonia.Media;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>Cell-local spatial tree. Detail is chosen in screen pixels; original polygons remain untouched.</summary>
internal sealed class GdsReviewSpatialGeometry
{
    internal sealed class Node
    {
        internal required Rect Bounds;
        internal Node? Left, Right;
        internal GdsPolygon? Polygon;
        internal int PolygonCount = 1;
    }

    internal Node? Root { get; }

    internal GdsReviewSpatialGeometry(IReadOnlyList<GdsPolygon> polygons)
    {
        var nodes = polygons.Where(p => p.Points.Count >= 3).Select(p => new Node
        {
            Polygon = p,
            Bounds = BoundsOf(p.Points)
        }).ToArray();
        Root = Build(nodes, 0, nodes.Length);
    }

    private static Node? Build(Node[] nodes, int start, int count)
    {
        if (count == 0) return null;
        if (count == 1) return nodes[start];
        var bounds = nodes[start].Bounds;
        for (int i = start + 1; i < start + count; i++) bounds = bounds.Union(nodes[i].Bounds);
        bool horizontal = bounds.Width >= bounds.Height;
        Array.Sort(nodes, start, count, Comparer<Node>.Create((a, b) =>
            (horizontal ? a.Bounds.Center.X : a.Bounds.Center.Y).CompareTo(horizontal ? b.Bounds.Center.X : b.Bounds.Center.Y)));
        int half = count / 2;
        return new Node { Bounds = bounds, PolygonCount = count, Left = Build(nodes, start, half), Right = Build(nodes, start + half, count - half) };
    }

    private static Rect BoundsOf(IReadOnlyList<GdsPoint> points)
    {
        double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Iterative closed-ring Douglas-Peucker, bounded error; preserves narrow corners.</summary>
    internal static IReadOnlyList<GdsPoint> Simplify(IReadOnlyList<GdsPoint> points, double tolerance)
    {
        if (points.Count <= 8 || tolerance <= 0) return points;
        int count = points.Count;
        if (points[0] == points[^1]) count--;
        int opposite = 1;
        double farthest = 0;
        for (int i = 1; i < count; i++)
        {
            double dx = points[i].X - points[0].X, dy = points[i].Y - points[0].Y;
            if (dx * dx + dy * dy > farthest) { farthest = dx * dx + dy * dy; opposite = i; }
        }
        var keep = new bool[count + 1];
        keep[0] = keep[opposite] = keep[count] = true;
        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, opposite)); stack.Push((opposite, count));
        GdsPoint At(int i) => points[i == count ? 0 : i];
        while (stack.TryPop(out var segment))
        {
            var a = At(segment.Start); var b = At(segment.End);
            double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
            double best = tolerance * tolerance;
            int selected = -1;
            for (int i = segment.Start + 1; i < segment.End; i++)
            {
                var p = At(i);
                double t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
                double ex = p.X - a.X - t * dx, ey = p.Y - a.Y - t * dy;
                double distance = ex * ex + ey * ey;
                if (distance > best) { best = distance; selected = i; }
            }
            if (selected < 0) continue;
            keep[selected] = true;
            stack.Push((segment.Start, selected)); stack.Push((selected, segment.End));
        }
        var result = new List<GdsPoint>();
        for (int i = 0; i < count; i++) if (keep[i]) result.Add(points[i]);
        return result.Count >= 3 ? result : points;
    }
}

/// <summary>Shared across cell placements, with absolute retained geometry and vertex limits.</summary>
internal sealed class GdsReviewGeometryLru
{
    private const int MaxEntries = 8192, MaxVertices = 1_000_000;
    private readonly Dictionary<(GdsReviewSpatialGeometry.Node Node, int Resolution), LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private int _vertices;
    private sealed record Entry((GdsReviewSpatialGeometry.Node Node, int Resolution) Key, Geometry Geometry, int Vertices);

    internal (Geometry Geometry, int Vertices) Get(GdsReviewSpatialGeometry.Node node, int resolution)
    {
        var key = (node, resolution);
        if (_entries.TryGetValue(key, out var cached))
        {
            _recent.Remove(cached); _recent.AddLast(cached);
            return (cached.Value.Geometry, cached.Value.Vertices);
        }
        int vertices = 0;
        var geometry = new StreamGeometry();
        using (var builder = geometry.Open())
        {
            builder.SetFillRule(FillRule.NonZero);
            Add(node);
            void Add(GdsReviewSpatialGeometry.Node current)
            {
                if (current.Polygon is null)
                {
                    if (current.Left is { } left) Add(left);
                    if (current.Right is { } right) Add(right);
                    return;
                }
                var points = current.Polygon.Points;
                if (resolution > 0) points = GdsReviewSpatialGeometry.Simplify(points, Math.Max(current.Bounds.Width, current.Bounds.Height) / resolution);
                vertices += points.Count;
                double area = 0;
                for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                    area += points[j].X * points[i].Y - points[i].X * points[j].Y;
                GdsPoint At(int i) => points[area < 0 ? points.Count - 1 - i : i];
                var first = At(0);
                builder.BeginFigure(new Point(first.X, first.Y), true);
                for (int i = 1; i < points.Count; i++) { var p = At(i); builder.LineTo(new Point(p.X, p.Y)); }
                builder.EndFigure(true);
            }
        }
        if (vertices <= MaxVertices)
        {
            while (_recent.First is { } first && (_entries.Count >= MaxEntries || _vertices + vertices > MaxVertices))
            {
                _entries.Remove(first.Value.Key); _vertices -= first.Value.Vertices; _recent.RemoveFirst();
            }
            var entry = new Entry(key, geometry, vertices);
            _entries.Add(key, _recent.AddLast(entry)); _vertices += vertices;
        }
        return (geometry, vertices);
    }
}

internal readonly record struct GdsReviewRenderStats(int Vertices, int Polygons, int Proxies, int CulledNodes);
