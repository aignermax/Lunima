namespace CAP_Core.Routing.PinBreakout;

/// <summary>
/// Exact arc–arc S-bend geometry that leads a wire out of a tight pin cluster: starting at
/// the pin heading, it shifts sideways by a lateral offset with two tangent-continuous arcs
/// of one radius and arrives parallel to the start heading, then runs straight to a common
/// forward distance. Offsets of at least twice the radius use two 90° arcs joined by a
/// lateral straight.
/// </summary>
public static class BreakoutSBend
{
    private const double DegreesToRadians = Math.PI / 180.0;
    private const double QuarterTurnDegrees = 90.0;

    /// <summary>Lateral offset (µm) below which the breakout is a plain straight.</summary>
    public const double StraightOffsetToleranceMicrometers = 0.01;

    /// <summary>Straight runs shorter than this (µm) are dropped as numerical noise.</summary>
    private const double MinStraightMicrometers = 1e-6;

    /// <summary>
    /// Forward length (µm) of the S-bend for lateral offset h and radius R:
    /// L = 2·√(R² − (R − h/2)²) for h &lt; 2R, and 2R (two quarter turns) beyond.
    /// </summary>
    public static double ForwardLength(double lateralOffset, double radius)
    {
        double h = Math.Abs(lateralOffset);
        if (h < StraightOffsetToleranceMicrometers) return 0;
        if (h >= 2 * radius) return 2 * radius;
        double half = radius - h / 2;
        return 2 * Math.Sqrt(radius * radius - half * half);
    }

    /// <summary>
    /// Builds the breakout from a pin outward: the S-bend (positive offset turns towards the
    /// heading + 90° side) followed by a straight up to <paramref name="forwardDistance"/>
    /// measured along the heading from the start point.
    /// </summary>
    /// <param name="x">Pin X (µm).</param>
    /// <param name="y">Pin Y (µm).</param>
    /// <param name="headingDegrees">Outward pin heading (degrees).</param>
    /// <param name="lateralOffset">Signed lateral shift (µm).</param>
    /// <param name="radius">Arc radius (µm).</param>
    /// <param name="forwardDistance">Total forward reach (µm); at least <see cref="ForwardLength"/>.</param>
    public static List<PathSegment> Build(
        double x, double y, double headingDegrees, double lateralOffset, double radius, double forwardDistance)
    {
        var segments = new List<PathSegment>();
        double h = Math.Abs(lateralOffset);
        if (h >= StraightOffsetToleranceMicrometers)
            AppendShift(segments, x, y, headingDegrees, lateralOffset, radius);

        var (endX, endY) = segments.Count > 0 ? segments[^1].EndPoint : (x, y);
        double headingRad = headingDegrees * DegreesToRadians;
        double travelled = (endX - x) * Math.Cos(headingRad) + (endY - y) * Math.Sin(headingRad);
        double remaining = forwardDistance - travelled;
        if (remaining > MinStraightMicrometers)
        {
            segments.Add(new StraightSegment(endX, endY,
                endX + remaining * Math.Cos(headingRad), endY + remaining * Math.Sin(headingRad), headingDegrees));
        }
        return segments;
    }

    private static void AppendShift(
        List<PathSegment> segments, double x, double y, double headingDegrees, double lateralOffset, double radius)
    {
        double sign = Math.Sign(lateralOffset);
        double h = Math.Abs(lateralOffset);
        double sweep = h >= 2 * radius
            ? QuarterTurnDegrees
            : Math.Acos(1 - h / (2 * radius)) / DegreesToRadians;

        var first = Arc(x, y, headingDegrees, sign * sweep, radius);
        segments.Add(first);
        var (cx, cy) = first.EndPoint;
        double sideHeading = headingDegrees + sign * sweep;

        double lateralStraight = h - 2 * radius;
        if (lateralStraight > MinStraightMicrometers)
        {
            double rad = sideHeading * DegreesToRadians;
            var straight = new StraightSegment(cx, cy,
                cx + lateralStraight * Math.Cos(rad), cy + lateralStraight * Math.Sin(rad), sideHeading);
            segments.Add(straight);
            (cx, cy) = straight.EndPoint;
        }
        segments.Add(Arc(cx, cy, sideHeading, -sign * sweep, radius));
    }

    /// <summary>An arc starting at (x, y) with the given tangent heading and signed sweep.</summary>
    private static BendSegment Arc(double x, double y, double headingDegrees, double sweepDegrees, double radius)
    {
        double rad = headingDegrees * DegreesToRadians;
        double side = Math.Sign(sweepDegrees);
        double centerX = x - Math.Sin(rad) * radius * side;
        double centerY = y + Math.Cos(rad) * radius * side;
        return new BendSegment(centerX, centerY, radius, headingDegrees, sweepDegrees);
    }
}
