namespace CAP_Core.Routing.ImportedGeometry;

/// <summary>Builds the reverse traversal of a single path segment.</summary>
public static class PathSegmentReversal
{
    private const double HalfTurnDegrees = 180.0;

    /// <summary>
    /// Returns a new segment covering the same geometry from its end point to its
    /// start point. A bend keeps its center and radius; its tangent turns around by
    /// 180° and its sweep changes sign.
    /// </summary>
    /// <param name="segment">The segment to reverse.</param>
    /// <exception cref="NotSupportedException">The segment type is unknown.</exception>
    public static PathSegment Reverse(PathSegment segment) => segment switch
    {
        BendSegment bend => new BendSegment(
            bend.Center.X, bend.Center.Y, bend.RadiusMicrometers,
            NormalizeDegrees(bend.EndAngleDegrees + HalfTurnDegrees),
            -bend.SweepAngleDegrees),
        StraightSegment straight => new StraightSegment(
            straight.EndPoint.X, straight.EndPoint.Y,
            straight.StartPoint.X, straight.StartPoint.Y,
            NormalizeDegrees(straight.StartAngleDegrees + HalfTurnDegrees)),
        _ => throw new NotSupportedException($"Cannot reverse segment type {segment.GetType().Name}."),
    };

    /// <summary>Maps an angle into [0, 360).</summary>
    public static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }
}
