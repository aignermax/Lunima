namespace CAP_Core.Routing;

/// <summary>
/// Base class for waveguide path segments.
/// </summary>
public abstract class PathSegment
{
    /// <summary>
    /// Length of this segment in micrometers.
    /// </summary>
    public abstract double LengthMicrometers { get; }

    /// <summary>
    /// Start point of the segment.
    /// </summary>
    public (double X, double Y) StartPoint { get; set; }

    /// <summary>
    /// End point of the segment.
    /// </summary>
    public (double X, double Y) EndPoint { get; set; }

    /// <summary>
    /// Angle at the start of the segment in degrees.
    /// </summary>
    public double StartAngleDegrees { get; set; }

    /// <summary>
    /// Angle at the end of the segment in degrees.
    /// </summary>
    public double EndAngleDegrees { get; set; }
}

/// <summary>
/// A straight waveguide segment.
/// </summary>
public class StraightSegment : PathSegment
{
    public override double LengthMicrometers
    {
        get
        {
            double dx = EndPoint.X - StartPoint.X;
            double dy = EndPoint.Y - StartPoint.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public StraightSegment(double startX, double startY, double endX, double endY, double angleDegrees)
    {
        StartPoint = (startX, startY);
        EndPoint = (endX, endY);
        StartAngleDegrees = angleDegrees;
        EndAngleDegrees = angleDegrees;
    }
}

/// <summary>
/// A circular arc bend segment.
/// </summary>
public class BendSegment : PathSegment
{
    /// <summary>
    /// Center point of the arc.
    /// </summary>
    public (double X, double Y) Center { get; set; }

    /// <summary>
    /// Radius of the bend in micrometers.
    /// </summary>
    public double RadiusMicrometers { get; set; }

    /// <summary>
    /// Sweep angle in degrees (positive = counter-clockwise, negative = clockwise).
    /// </summary>
    public double SweepAngleDegrees { get; set; }

    public override double LengthMicrometers => Math.Abs(SweepAngleDegrees) * Math.PI / 180.0 * RadiusMicrometers;

    /// <summary>
    /// Number of equivalent 90-degree bends (for loss calculation).
    /// </summary>
    public double Equivalent90DegreeBends => Math.Abs(SweepAngleDegrees) / 90.0;

    public BendSegment(double centerX, double centerY, double radius, double startAngle, double sweepAngle)
    {
        Center = (centerX, centerY);
        RadiusMicrometers = radius;
        SweepAngleDegrees = sweepAngle;
        StartAngleDegrees = startAngle;
        EndAngleDegrees = startAngle + sweepAngle;

        // Calculate start and end points from arc geometry
        double startRad = startAngle * Math.PI / 180.0;
        double endRad = (startAngle + sweepAngle) * Math.PI / 180.0;

        // Points are perpendicular to the angle (tangent direction)
        StartPoint = (centerX + radius * Math.Cos(startRad - Math.PI / 2 * Math.Sign(sweepAngle)),
                      centerY + radius * Math.Sin(startRad - Math.PI / 2 * Math.Sign(sweepAngle)));
        EndPoint = (centerX + radius * Math.Cos(endRad - Math.PI / 2 * Math.Sign(sweepAngle)),
                    centerY + radius * Math.Sin(endRad - Math.PI / 2 * Math.Sign(sweepAngle)));
    }
}
