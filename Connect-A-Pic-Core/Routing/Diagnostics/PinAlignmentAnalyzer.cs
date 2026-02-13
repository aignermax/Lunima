namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Analyzes the geometric alignment between two pins for diagnostics.
/// </summary>
public static class PinAlignmentAnalyzer
{
    /// <summary>
    /// Tolerance in degrees for considering angles aligned.
    /// </summary>
    private const double AngleToleranceDegrees = 5.0;

    /// <summary>
    /// Tolerance in micrometers for considering pins collinear.
    /// </summary>
    private const double CollinearToleranceMicrometers = 1.0;

    /// <summary>
    /// Analyzes the geometric relationship between start and end pin positions.
    /// </summary>
    /// <param name="startX">Start pin X position in micrometers.</param>
    /// <param name="startY">Start pin Y position in micrometers.</param>
    /// <param name="startAngle">Start pin angle in degrees.</param>
    /// <param name="endX">End pin X position in micrometers.</param>
    /// <param name="endY">End pin Y position in micrometers.</param>
    /// <param name="endInputAngle">End pin input angle in degrees.</param>
    /// <returns>Pin alignment information.</returns>
    public static PinAlignmentInfo Analyze(
        double startX, double startY, double startAngle,
        double endX, double endY, double endInputAngle)
    {
        double dx = endX - startX;
        double dy = endY - startY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        double startRad = startAngle * Math.PI / 180.0;
        double fwdX = Math.Cos(startRad);
        double fwdY = Math.Sin(startRad);

        double forwardDist = dx * fwdX + dy * fwdY;
        double lateralDist = dx * (-fwdY) + dy * fwdX;

        double angleDiff = NormalizeAngle(endInputAngle - startAngle);

        bool areCollinear = Math.Abs(lateralDist) < CollinearToleranceMicrometers;
        bool areFacing = Math.Abs(angleDiff) < AngleToleranceDegrees
                         || Math.Abs(angleDiff - 360) < AngleToleranceDegrees;

        return new PinAlignmentInfo
        {
            DistanceMicrometers = distance,
            ForwardDistanceMicrometers = forwardDist,
            LateralOffsetMicrometers = lateralDist,
            AngleDifferenceDegrees = angleDiff,
            AreCollinear = areCollinear,
            AreFacing = areFacing
        };
    }

    /// <summary>
    /// Normalizes angle to 0-360 range.
    /// </summary>
    private static double NormalizeAngle(double degrees)
    {
        while (degrees < 0) degrees += 360;
        while (degrees >= 360) degrees -= 360;
        return degrees;
    }
}
