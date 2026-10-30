namespace CAP_Core.Routing.MeanderGeneration.Clearance;

/// <summary>
/// Exact 2-D primitives behind the clearance checks: chord-tolerant path sampling, segment
/// distances, segment-in-rectangle tests and clipping of a convex polygon to a band of a local
/// frame (used to find where along a straight a fold has room).
/// </summary>
internal static class ClearanceGeometry
{
    /// <summary>Maximum distance (µm) between an arc and its sampled chords.</summary>
    private const double ChordToleranceMicrometers = 0.02;
    private const int MinArcPieces = 2;
    private const double Eps = 1e-12;

    /// <summary>The path as a polyline: straights stay single chords, arcs are chord-sampled.</summary>
    public static List<(double X, double Y)> Sample(RoutedPath path)
    {
        var points = new List<(double X, double Y)>();
        foreach (var segment in path.Segments)
        {
            if (points.Count == 0)
                points.Add(segment.StartPoint);
            if (segment is BendSegment bend)
                AppendArc(points, bend);
            else
                points.Add(segment.EndPoint);
        }
        return points;
    }

    private static void AppendArc(List<(double X, double Y)> points, BendSegment bend)
    {
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180.0;
        double r = bend.RadiusMicrometers;
        double maxStep = r > ChordToleranceMicrometers
            ? 2.0 * Math.Acos(1.0 - ChordToleranceMicrometers / r)
            : Math.PI / 2.0;
        int pieces = Math.Max(MinArcPieces, (int)Math.Ceiling(Math.Abs(sweepRad) / maxStep));
        double sign = Math.Sign(bend.SweepAngleDegrees) == 0 ? 1 : Math.Sign(bend.SweepAngleDegrees);
        double startRad = bend.StartAngleDegrees * Math.PI / 180.0 - Math.PI / 2.0 * sign;
        for (int i = 1; i <= pieces; i++)
        {
            double angle = startRad + sweepRad * i / pieces;
            points.Add((bend.Center.X + r * Math.Cos(angle), bend.Center.Y + r * Math.Sin(angle)));
        }
    }

    /// <summary>
    /// Distance between segments ab and cd, and the point on ab closest to cd.
    /// </summary>
    public static (double Distance, double X, double Y) SegmentDistance(
        double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        if (SegmentsIntersect(ax, ay, bx, by, cx, cy, dx, dy))
        {
            var (px, py) = ClosestOnSegment(ax, ay, bx, by, cx, cy);
            return (0.0, px, py);
        }

        var best = (Distance: double.MaxValue, X: ax, Y: ay);
        Consider(ref best, ClosestOnSegment(ax, ay, bx, by, cx, cy), cx, cy);
        Consider(ref best, ClosestOnSegment(ax, ay, bx, by, dx, dy), dx, dy);
        var (qx, qy) = ClosestOnSegment(cx, cy, dx, dy, ax, ay);
        Consider(ref best, (ax, ay), qx, qy);
        (qx, qy) = ClosestOnSegment(cx, cy, dx, dy, bx, by);
        Consider(ref best, (bx, by), qx, qy);
        return best;
    }

    private static void Consider(ref (double Distance, double X, double Y) best,
        (double X, double Y) onFirst, double otherX, double otherY)
    {
        double distance = Math.Sqrt(Square(onFirst.X - otherX) + Square(onFirst.Y - otherY));
        if (distance < best.Distance)
            best = (distance, onFirst.X, onFirst.Y);
    }

    private static (double X, double Y) ClosestOnSegment(double ax, double ay, double bx, double by, double px, double py)
    {
        double vx = bx - ax;
        double vy = by - ay;
        double lengthSquared = vx * vx + vy * vy;
        if (lengthSquared < Eps)
            return (ax, ay);
        double t = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / lengthSquared, 0.0, 1.0);
        return (ax + t * vx, ay + t * vy);
    }

    private static bool SegmentsIntersect(
        double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double d1 = Cross(cx, cy, dx, dy, ax, ay);
        double d2 = Cross(cx, cy, dx, dy, bx, by);
        double d3 = Cross(ax, ay, bx, by, cx, cy);
        double d4 = Cross(ax, ay, bx, by, dx, dy);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross(double ax, double ay, double bx, double by, double px, double py)
        => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    /// <summary>True when any part of segment ab lies inside the closed rectangle (Liang–Barsky).</summary>
    public static bool SegmentTouchesRectangle(double ax, double ay, double bx, double by, MeanderBounds rect)
    {
        double t0 = 0.0;
        double t1 = 1.0;
        double vx = bx - ax;
        double vy = by - ay;
        return Clip(-vx, ax - rect.MinX, ref t0, ref t1) && Clip(vx, rect.MaxX - ax, ref t0, ref t1)
            && Clip(-vy, ay - rect.MinY, ref t0, ref t1) && Clip(vy, rect.MaxY - ay, ref t0, ref t1);
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < Eps)
            return q >= 0;
        double r = q / p;
        if (p < 0)
        {
            if (r > t1) return false;
            t0 = Math.Max(t0, r);
        }
        else
        {
            if (r < t0) return false;
            t1 = Math.Min(t1, r);
        }
        return true;
    }

    /// <summary>
    /// Clips a convex polygon (or a 2-point segment) given in local (t, v) coordinates to the
    /// band <paramref name="vLow"/> ≤ v ≤ <paramref name="vHigh"/>; returns the t-extent of what
    /// remains, or false when nothing does.
    /// </summary>
    public static bool BandExtent(IReadOnlyList<(double T, double V)> polygon, double vLow, double vHigh,
        out double tMin, out double tMax)
    {
        tMin = double.MaxValue;
        tMax = double.MinValue;
        int count = polygon.Count;
        int edges = count == 2 ? 1 : count;
        for (int i = 0; i < edges; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % count];
            if (!ClipToBand(a, b, vLow, vHigh, out double ta, out double tb))
                continue;
            tMin = Math.Min(tMin, Math.Min(ta, tb));
            tMax = Math.Max(tMax, Math.Max(ta, tb));
        }
        if (count > 2)
            ExtendByInteriorCrossings(polygon, vLow, vHigh, ref tMin, ref tMax);
        return tMin <= tMax;
    }

    private static bool ClipToBand((double T, double V) a, (double T, double V) b, double vLow, double vHigh,
        out double ta, out double tb)
    {
        ta = tb = 0;
        double s0 = 0.0;
        double s1 = 1.0;
        double dv = b.V - a.V;
        if (!Clip(-dv, a.V - vLow, ref s0, ref s1) || !Clip(dv, vHigh - a.V, ref s0, ref s1))
            return false;
        ta = a.T + (b.T - a.T) * s0;
        tb = a.T + (b.T - a.T) * s1;
        return true;
    }

    /// <summary>
    /// A polygon spanning the whole band has edges that miss it while its interior covers it:
    /// the band's boundary lines cut the polygon, and those cuts extend the t-extent.
    /// </summary>
    private static void ExtendByInteriorCrossings(IReadOnlyList<(double T, double V)> polygon,
        double vLow, double vHigh, ref double tMin, ref double tMax)
    {
        foreach (double v in new[] { vLow, vHigh })
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                if ((a.V - v) * (b.V - v) > 0 || Math.Abs(b.V - a.V) < Eps)
                    continue;
                double t = a.T + (b.T - a.T) * (v - a.V) / (b.V - a.V);
                tMin = Math.Min(tMin, t);
                tMax = Math.Max(tMax, t);
            }
        }
    }

    private static double Square(double value) => value * value;
}
