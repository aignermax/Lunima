using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

internal static class OutlineGeometryLod
{
    // Rendering-only Douglas–Peucker simplification. Preserve bounding extrema so
    // footprints, thin waveguides and the raster cache's coordinate frame stay fixed.
    internal static IReadOnlyList<OutlinePoint> Simplify(IReadOnlyList<OutlinePoint> points, double tolerance)
    {
        if (tolerance <= 0 || points.Count <= 5) return points;
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].X < points[minX].X) minX = i;
            if (points[i].X > points[maxX].X) maxX = i;
            if (points[i].Y < points[minY].Y) minY = i;
            if (points[i].Y > points[maxY].Y) maxY = i;
        }
        keep[minX] = keep[maxX] = keep[minY] = keep[maxY] = true;
        var pending = new Stack<(int Start, int End)>();
        int start = 0;
        for (int i = 1; i < keep.Length; i++)
            if (keep[i]) { pending.Push((start, i)); start = i; }
        double toleranceSquared = tolerance * tolerance;
        // Sawtooth rings can make Douglas–Peucker peel one vertex per split.
        // Bound UI-thread work linearly; exact geometry is always a safe fallback.
        long remainingPointVisits = 32L * points.Count;
        while (pending.TryPop(out var range))
        {
            var a = points[range.Start]; var b = points[range.End];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            double maximum = toleranceSquared;
            int split = -1;
            for (int i = range.Start + 1; i < range.End; i++)
            {
                if (remainingPointVisits-- <= 0) return points;
                double px = points[i].X - a.X, py = points[i].Y - a.Y;
                double t = lengthSquared == 0 ? 0 : Math.Clamp((px * dx + py * dy) / lengthSquared, 0, 1);
                double ex = px - t * dx, ey = py - t * dy;
                double distance = ex * ex + ey * ey;
                if (distance > maximum) { maximum = distance; split = i; }
            }
            if (split < 0) continue;
            keep[split] = true;
            pending.Push((range.Start, split));
            pending.Push((split, range.End));
        }
        var result = new List<OutlinePoint>();
        for (int i = 0; i < points.Count; i++) if (keep[i]) result.Add(points[i]);
        if (result.Count < 4 || result.Count >= points.Count) return points;
        // A subpixel-wide core may have a large bounding box. A positional error
        // alone can collapse its two boundaries, so retain the exact outline if
        // simplification would change winding or remove/add appreciable area.
        double originalArea = SignedArea(points), simplifiedArea = SignedArea(result);
        return Math.Abs(simplifiedArea - originalArea) <= Math.Abs(originalArea) * 0.05
            && originalArea != 0 ? result : points;
    }

    private static double SignedArea(IReadOnlyList<OutlinePoint> points)
    {
        // Subtract a local origin to avoid cancellation for tiny cores at large coordinates.
        var origin = points[0];
        double area = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            area += (points[j].X - origin.X) * (points[i].Y - origin.Y)
                - (points[i].X - origin.X) * (points[j].Y - origin.Y);
        return area * 0.5;
    }
}
