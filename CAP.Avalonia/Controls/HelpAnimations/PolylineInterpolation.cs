using System;
using System.Collections.Generic;
using Avalonia;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>Length-proportional interpolation along a polyline, shared by the help animations.</summary>
internal static class PolylineInterpolation
{
    /// <summary>
    /// Returns the point at fraction <paramref name="t"/> (0 = first point, 1 = last point)
    /// along the polyline, measured by accumulated segment length. With fewer than two
    /// points the origin is returned.
    /// </summary>
    public static Point PointAt(IReadOnlyList<Point> points, double t)
    {
        if (points.Count == 0)
            return default;
        if (points.Count == 1 || t <= 0)
            return points[0];
        if (t >= 1)
            return points[points.Count - 1];

        double total = TotalLength(points);
        if (total <= 0)
            return points[0];

        double remaining = total * t;
        for (int i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            double segment = Distance(from, to);
            if (remaining > segment)
            {
                remaining -= segment;
                continue;
            }
            double local = segment <= 0 ? 0 : remaining / segment;
            return new Point(from.X + (to.X - from.X) * local, from.Y + (to.Y - from.Y) * local);
        }
        return points[points.Count - 1];
    }

    private static double TotalLength(IReadOnlyList<Point> points)
    {
        double total = 0;
        for (int i = 1; i < points.Count; i++)
            total += Distance(points[i - 1], points[i]);
        return total;
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
