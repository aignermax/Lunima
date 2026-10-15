namespace CAP_Core.Routing.PinBreakout;

/// <summary>Samples straight and arc segments into points at most a given step apart.</summary>
internal static class SegmentSampling
{
    /// <summary>Points along <paramref name="segments"/>, including every segment's end points.</summary>
    public static IEnumerable<(double X, double Y)> Points(IEnumerable<PathSegment> segments, double step)
    {
        foreach (var segment in segments)
        {
            if (segment is BendSegment bend)
            {
                foreach (var point in ArcSampling.SamplePoints(bend, step)) yield return point;
                continue;
            }
            double length = segment.LengthMicrometers;
            int steps = Math.Max(1, (int)Math.Ceiling(length / step));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                yield return (segment.StartPoint.X + (segment.EndPoint.X - segment.StartPoint.X) * t,
                              segment.StartPoint.Y + (segment.EndPoint.Y - segment.StartPoint.Y) * t);
            }
        }
    }
}
