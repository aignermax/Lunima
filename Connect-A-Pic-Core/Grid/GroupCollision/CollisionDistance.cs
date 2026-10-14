namespace CAP_Core.Grid.GroupCollision;

/// <summary>
/// Exact 2D distances between straight segments and axis-aligned rectangles, the
/// primitives of the placement collision test. Zero means touching or intersecting.
/// </summary>
internal static class CollisionDistance
{
    /// <summary>Distance between two segments (0 when they intersect or touch).</summary>
    public static double SegmentToSegment(
        double ax, double ay, double bx, double by,
        double cx, double cy, double dx, double dy)
    {
        if (SegmentsIntersect(ax, ay, bx, by, cx, cy, dx, dy))
            return 0;
        return Math.Min(
            Math.Min(PointToSegment(ax, ay, cx, cy, dx, dy), PointToSegment(bx, by, cx, cy, dx, dy)),
            Math.Min(PointToSegment(cx, cy, ax, ay, bx, by), PointToSegment(dx, dy, ax, ay, bx, by)));
    }

    /// <summary>Distance between a segment and a closed rectangle (0 when the segment touches or enters it).</summary>
    public static double SegmentToRect(
        double ax, double ay, double bx, double by,
        double minX, double minY, double maxX, double maxY)
    {
        if (Contains(ax, ay, minX, minY, maxX, maxY) || Contains(bx, by, minX, minY, maxX, maxY))
            return 0;
        if (SegmentsIntersect(ax, ay, bx, by, minX, minY, maxX, minY)
            || SegmentsIntersect(ax, ay, bx, by, maxX, minY, maxX, maxY)
            || SegmentsIntersect(ax, ay, bx, by, maxX, maxY, minX, maxY)
            || SegmentsIntersect(ax, ay, bx, by, minX, maxY, minX, minY))
            return 0;

        // Disjoint convex shapes: the closest pair involves a segment endpoint or a rectangle corner.
        double best = Math.Min(
            PointToRect(ax, ay, minX, minY, maxX, maxY),
            PointToRect(bx, by, minX, minY, maxX, maxY));
        best = Math.Min(best, PointToSegment(minX, minY, ax, ay, bx, by));
        best = Math.Min(best, PointToSegment(maxX, minY, ax, ay, bx, by));
        best = Math.Min(best, PointToSegment(maxX, maxY, ax, ay, bx, by));
        return Math.Min(best, PointToSegment(minX, maxY, ax, ay, bx, by));
    }

    /// <summary>Distance from a point to a closed rectangle (0 inside).</summary>
    public static double PointToRect(double px, double py, double minX, double minY, double maxX, double maxY)
    {
        double dx = Math.Max(Math.Max(minX - px, 0), px - maxX);
        double dy = Math.Max(Math.Max(minY - py, 0), py - maxY);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Distance from a point to a segment.</summary>
    public static double PointToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double vx = bx - ax, vy = by - ay;
        double lengthSquared = (vx * vx) + (vy * vy);
        double t = lengthSquared <= 0 ? 0 : (((px - ax) * vx) + ((py - ay) * vy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        double qx = ax + (t * vx) - px, qy = ay + (t * vy) - py;
        return Math.Sqrt((qx * qx) + (qy * qy));
    }

    private static bool Contains(double px, double py, double minX, double minY, double maxX, double maxY) =>
        px >= minX && px <= maxX && py >= minY && py <= maxY;

    /// <summary>True when the closed segments share at least one point (collinear overlaps included).</summary>
    private static bool SegmentsIntersect(
        double ax, double ay, double bx, double by,
        double cx, double cy, double dx, double dy)
    {
        double d1 = Cross(cx, cy, dx, dy, ax, ay);
        double d2 = Cross(cx, cy, dx, dy, bx, by);
        double d3 = Cross(ax, ay, bx, by, cx, cy);
        double d4 = Cross(ax, ay, bx, by, dx, dy);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;
        return (d1 == 0 && OnSegment(cx, cy, dx, dy, ax, ay))
            || (d2 == 0 && OnSegment(cx, cy, dx, dy, bx, by))
            || (d3 == 0 && OnSegment(ax, ay, bx, by, cx, cy))
            || (d4 == 0 && OnSegment(ax, ay, bx, by, dx, dy));
    }

    private static double Cross(double ax, double ay, double bx, double by, double px, double py) =>
        ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));

    private static bool OnSegment(double ax, double ay, double bx, double by, double px, double py) =>
        px >= Math.Min(ax, bx) && px <= Math.Max(ax, bx) && py >= Math.Min(ay, by) && py <= Math.Max(ay, by);
}
