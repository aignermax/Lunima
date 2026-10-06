using Avalonia;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Geometry-accurate hit test for components drawn from imported outline polygons. Their
/// bounding box can be far larger than what is drawn — a composite cell spans
/// millimetres while waveguides run through its empty space — so a click must land on
/// (or within <see cref="ToleranceUm"/> of) a drawn polygon to hit the component.
/// Components without outlines keep the plain box test.
/// </summary>
internal static class OutlineHitTester
{
    /// <summary>How close (µm) to a drawn polygon a click still hits it.</summary>
    public const double ToleranceUm = 2.0;

    /// <summary>True when <paramref name="point"/> hits <paramref name="component"/>'s drawn geometry.</summary>
    public static bool Hits(Component component, Point point)
    {
        if (component.OutlinePolygons is not { Count: > 0 } outlines)
            return true;
        foreach (var polygon in outlines)
        {
            var world = ComponentOutlineRenderer.ComputeWorldPoints(polygon,
                component.PhysicalX, component.PhysicalY, component.WidthMicrometers, component.HeightMicrometers,
                component.RotationDegrees, component.UnrotatedWidthMicrometers, component.UnrotatedHeightMicrometers,
                component.IsMirroredHorizontally);
            if (world.Length >= 3 && (Inside(world, point) || NearEdge(world, point)))
                return true;
        }
        return false;
    }

    private static bool Inside(Point[] ring, Point p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            if ((ring[i].Y > p.Y) != (ring[j].Y > p.Y)
                && p.X < (ring[j].X - ring[i].X) * (p.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X)
                inside = !inside;
        }
        return inside;
    }

    private static bool NearEdge(Point[] ring, Point p)
    {
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            if (DistanceToSegment(p, ring[j], ring[i]) <= ToleranceUm)
                return true;
        }
        return false;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared <= 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
        return Math.Sqrt(cx * cx + cy * cy);
    }
}
