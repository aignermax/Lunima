using Avalonia;
using Avalonia.Media;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Batches outline polygons into few geometries: one per (layer, datatype, size
/// class). A full-chip import carries tens of thousands of polygons; issuing one
/// draw call per polygon made a zoomed-out frame take ~190 ms. Polygons are wound
/// the same way and filled non-zero, so overlapping polygons of a layer union
/// instead of punching even-odd holes. The size class keeps the level-of-detail
/// cull exact: a batch is skipped only when its LARGEST polygon is below a pixel.
/// </summary>
internal static class OutlineGeometryBatcher
{
    /// <summary>
    /// Builds the batches in first-appearance order of their (layer, datatype), so the
    /// layer stacking matches the polygon order of the source list.
    /// </summary>
    public static OutlineBatch[] Build(IReadOnlyList<OutlinePolygon> outlines)
    {
        var groups = new Dictionary<(int Layer, int DataType, int SizeClass, long Tile), List<OutlinePolygon>>();
        var order = new List<(int, int, int, long)>();
        foreach (var polygon in outlines)
        {
            if (polygon.Points.Count < 3) continue;
            var key = (polygon.Layer, polygon.DataType, SizeClass(polygon), Tile(polygon));
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = new List<OutlinePolygon>();
                order.Add(key);
            }
            list.Add(polygon);
        }

        return order
            .OrderBy(k => FirstIndexOfLayer(order, k.Item1, k.Item2))
            .Select(k => CreateBatch(k.Item1, k.Item2, groups[k]))
            .ToArray();
    }

    private static int FirstIndexOfLayer(List<(int, int, int, long)> order, int layer, int dataType) =>
        order.FindIndex(o => o.Item1 == layer && o.Item2 == dataType);

    /// <summary>Edge (um) of the spatial tiles batches are split into, so off-screen parts of a huge cell are skipped.</summary>
    public const double TileUm = 500;

    /// <summary>Tile of the polygon's first vertex, packed into one key.</summary>
    private static long Tile(OutlinePolygon polygon)
    {
        long tx = (long)Math.Floor(polygon.Points[0].X / TileUm), ty = (long)Math.Floor(polygon.Points[0].Y / TileUm);
        return (tx << 32) ^ (ty & 0xffffffffL);
    }

    private static OutlineBatch CreateBatch(int layer, int dataType, List<OutlinePolygon> polygons)
    {
        var geometry = new StreamGeometry();
        double maxExtent = 0;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);
            foreach (var polygon in polygons)
            {
                var points = polygon.Points;
                bool reverse = SignedArea(points) < 0;
                AddFigure(ctx, points, reverse);
                var (pMinX, pMinY, pMaxX, pMaxY) = Bounds(points);
                maxExtent = Math.Max(maxExtent, Math.Max(pMaxX - pMinX, pMaxY - pMinY));
                minX = Math.Min(minX, pMinX); minY = Math.Min(minY, pMinY);
                maxX = Math.Max(maxX, pMaxX); maxY = Math.Max(maxY, pMaxY);
            }
        }
        var (fill, outline) = OutlineLayerPalette.OutlineStyleFor(layer, dataType);
        return new OutlineBatch(geometry, new Rect(minX, minY, maxX - minX, maxY - minY),
            maxExtent, polygons.Count, fill, outline, layer, dataType);
    }

    private static void AddFigure(StreamGeometryContext ctx, IReadOnlyList<OutlinePoint> points, bool reverse)
    {
        int n = points.Count;
        Point At(int i) => reverse ? new Point(points[n - 1 - i].X, points[n - 1 - i].Y) : new Point(points[i].X, points[i].Y);
        ctx.BeginFigure(At(0), true);
        for (int i = 1; i < n; i++)
            ctx.LineTo(At(i));
        ctx.EndFigure(true);
    }

    /// <summary>Size class: ⌊log2(largest extent in µm)⌋, so a class spans a factor of two.</summary>
    private static int SizeClass(OutlinePolygon polygon)
    {
        var (minX, minY, maxX, maxY) = Bounds(polygon.Points);
        double extent = Math.Max(maxX - minX, maxY - minY);
        return extent <= 0 ? int.MinValue : (int)Math.Floor(Math.Log2(extent));
    }

    private static double SignedArea(IReadOnlyList<OutlinePoint> points)
    {
        double area = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            area += (points[j].X * points[i].Y) - (points[i].X * points[j].Y);
        return area / 2.0;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IReadOnlyList<OutlinePoint> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        return (minX, minY, maxX, maxY);
    }
}

/// <summary>
/// One batched draw: the merged geometry of a layer's polygons of one size class, its
/// local-frame bounds, the largest member polygon's extent (for the LOD cull), the
/// member count, the per-layer style and the source (layer, datatype).
/// </summary>
internal sealed record OutlineBatch(
    StreamGeometry Geometry,
    Rect Bounds,
    double MaxPolygonExtent,
    int PolygonCount,
    IBrush Fill,
    Pen Outline,
    int Layer,
    int DataType);
