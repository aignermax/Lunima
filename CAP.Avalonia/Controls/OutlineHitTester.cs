using System.Runtime.CompilerServices;
using Avalonia;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Geometry-accurate hit test for components drawn from imported outline polygons. Their
/// bounding box can be far larger than what is drawn — a composite cell spans
/// millimetres while waveguides run through its empty space — so a click must land on
/// (or within the tolerance of) a drawn polygon to hit the component. Components without
/// outlines keep the plain box test. The cursor is mapped once into the component's local
/// frame and polygons are prefiltered by cached bounding boxes, so hovering over a cell
/// with a million outline vertices stays cheap.
/// </summary>
internal static class OutlineHitTester
{
    /// <summary>Smallest hit distance (µm) to a drawn polygon.</summary>
    public const double MinToleranceUm = 2.0;

    /// <summary>Screen distance (px) within which a drawn polygon still counts as hit.</summary>
    public const double TolerancePx = 4.0;

    private static readonly ConditionalWeakTable<IReadOnlyList<OutlinePolygon>, Rect[]> BoundsCache = new();

    /// <summary>The hit tolerance (µm) at <paramref name="zoom"/> screen px per µm.</summary>
    public static double ToleranceAt(double zoom) => Math.Max(MinToleranceUm, TolerancePx / Math.Max(zoom, 1e-6));

    /// <summary>True when <paramref name="point"/> (world µm) hits <paramref name="component"/>'s drawn geometry.</summary>
    /// <param name="component">The component under test.</param>
    /// <param name="point">Cursor position in world coordinates.</param>
    /// <param name="toleranceUm">How close to a polygon still counts (see <see cref="ToleranceAt"/>).</param>
    public static bool Hits(Component component, Point point, double toleranceUm)
    {
        if (component is ComponentGroup || component.OutlinePolygons is not { Count: > 0 } outlines)
            return true;
        if (LocalPoint(component, point) is not { } local)
            return true;

        var bounds = BoundsCache.GetValue(outlines, ComputeBounds);
        for (int i = 0; i < outlines.Count; i++)
        {
            if (!bounds[i].Inflate(toleranceUm).Contains(local)) continue;
            var ring = outlines[i].Points;
            if (ring.Count >= 3 && (Inside(ring, local) || NearEdge(ring, local, toleranceUm)))
                return true;
        }
        return false;
    }

    /// <summary>The world point in the component's unrotated, unmirrored outline frame (inverse of the renderer's pose).</summary>
    private static Point? LocalPoint(Component c, Point world)
    {
        var dest = GdsPolygonRenderer.GetUnrotatedDestRect(c.PhysicalX, c.PhysicalY, c.WidthMicrometers, c.HeightMicrometers,
            c.RotationDegrees, c.UnrotatedWidthMicrometers, c.UnrotatedHeightMicrometers);
        var mirror = c.IsMirroredHorizontally ? new Matrix(1, 0, 0, -1, 0, dest.Height) : Matrix.Identity;
        var localToWorld = mirror * Matrix.CreateTranslation(dest.X, dest.Y)
            * GdsPolygonRenderer.BuildRotationMatrix(c.RotationDegrees,
                c.PhysicalX + c.WidthMicrometers / 2.0, c.PhysicalY + c.HeightMicrometers / 2.0);
        return localToWorld.TryInvert(out var worldToLocal) ? world.Transform(worldToLocal) : null;
    }

    private static Rect[] ComputeBounds(IReadOnlyList<OutlinePolygon> outlines) =>
        outlines.Select(p =>
        {
            if (p.Points.Count == 0) return default;
            double minX = p.Points.Min(q => q.X), minY = p.Points.Min(q => q.Y);
            return new Rect(minX, minY, p.Points.Max(q => q.X) - minX, p.Points.Max(q => q.Y) - minY);
        }).ToArray();

    private static bool Inside(IReadOnlyList<OutlinePoint> ring, Point p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            if ((ring[i].Y > p.Y) != (ring[j].Y > p.Y)
                && p.X < (ring[j].X - ring[i].X) * (p.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X)
                inside = !inside;
        }
        return inside;
    }

    private static bool NearEdge(IReadOnlyList<OutlinePoint> ring, Point p, double tolerance)
    {
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            if (DistanceToSegment(p, ring[j], ring[i]) <= tolerance)
                return true;
        }
        return false;
    }

    private static double DistanceToSegment(Point p, OutlinePoint a, OutlinePoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared <= 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
        return Math.Sqrt(cx * cx + cy * cy);
    }
}
