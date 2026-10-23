namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Immutable registration values for a rasterized obstacle. Live route segments can be
/// edited in place, so retaining their references cannot identify an unchanged raster.
/// </summary>
internal sealed class WaveguideObstacleShape
{
    private readonly SegmentValue[] _segments;
    private readonly double _width;

    internal WaveguideObstacleShape(IReadOnlyList<PathSegment> segments, double width)
    {
        _segments = segments.Select(SegmentValue.From).ToArray();
        _width = width;
    }

    internal bool Matches(IReadOnlyList<PathSegment> segments, double width)
    {
        if (_width != width || _segments.Length != segments.Count) return false;
        for (var i = 0; i < segments.Count; i++)
            if (_segments[i] != SegmentValue.From(segments[i])) return false;
        return true;
    }

    private readonly record struct SegmentValue(Type Type, (double X, double Y) Start,
        (double X, double Y) End, double StartAngle, double EndAngle,
        (double X, double Y) Center, double Radius, double Sweep)
    {
        internal static SegmentValue From(PathSegment segment) => new(
            segment.GetType(), segment.StartPoint, segment.EndPoint,
            segment.StartAngleDegrees, segment.EndAngleDegrees,
            segment is BendSegment bend ? bend.Center : default,
            segment is BendSegment bendRadius ? bendRadius.RadiusMicrometers : 0,
            segment is BendSegment bendSweep ? bendSweep.SweepAngleDegrees : 0);
    }
}
