using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.GeometricSolvers;

/// <summary>
/// Validates geometric properties of paths and segments.
/// </summary>
public class GeometricValidator
{
    private const double PositionTolerance = 1.0; // micrometers
    private const double AngleTolerance = 5.0; // degrees

    /// <summary>
    /// Validates that the first arc actually starts at the start pin position.
    /// </summary>
    public bool ValidateStartPoint(BendSegment bend, double startX, double startY, out double error)
    {
        error = Math.Sqrt(
            Math.Pow(bend.StartPoint.X - startX, 2) +
            Math.Pow(bend.StartPoint.Y - startY, 2));

        return error <= PositionTolerance;
    }

    /// <summary>
    /// Validates that two arcs connect properly (first arc end = second arc start).
    /// </summary>
    public bool ValidateContinuity(BendSegment bend1, BendSegment bend2, out double error)
    {
        error = Math.Sqrt(
            Math.Pow(bend1.EndPoint.X - bend2.StartPoint.X, 2) +
            Math.Pow(bend1.EndPoint.Y - bend2.StartPoint.Y, 2));

        return error <= PositionTolerance;
    }

    /// <summary>
    /// Validates that the second arc ends at the end pin position.
    /// </summary>
    public bool ValidateEndpoint(BendSegment bend, double endX, double endY, out double error)
    {
        error = Math.Sqrt(
            Math.Pow(bend.EndPoint.X - endX, 2) +
            Math.Pow(bend.EndPoint.Y - endY, 2));

        return error <= PositionTolerance;
    }

    /// <summary>
    /// Validates that two angles are aligned (for straight-line detection).
    /// </summary>
    public bool ValidateAngleAlignment(double angle1, double angle2)
    {
        double diff = Math.Abs(NormalizeAngle(angle1 - angle2));
        return diff <= AngleTolerance;
    }

    /// <summary>
    /// Validates that a line from (x1,y1) to (x2,y2) is aligned with a given angle.
    /// </summary>
    public bool ValidateLineAlignment(
        double x1, double y1, double x2, double y2,
        double expectedAngle)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 1.0) return false; // Too close

        double lineAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        return ValidateAngleAlignment(lineAngle, expectedAngle);
    }

    /// <summary>
    /// Normalizes an angle to the range [-180, 180).
    /// </summary>
    private double NormalizeAngle(double angle)
    {
        while (angle >= 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }
}
