using CAP_Core.Routing.Utilities;
namespace CAP_Core.Routing.Utilities;

/// <summary>
/// Centralized angle manipulation utilities with consistent tolerances.
/// Eliminates scattered angle logic and magic number tolerances.
/// </summary>
public static class AngleUtilities
{
    /// <summary>
    /// Default angular tolerance for comparing angles (in degrees).
    /// Used consistently across all angle comparisons.
    /// </summary>
    public const double DefaultAngleTolerance = 10.0;

    /// <summary>
    /// Normalizes an angle to the range (-range, range].
    /// </summary>
    /// <param name="angle">Angle in degrees</param>
    /// <param name="range">Range limit (default 180 for [-180, 180])</param>
    /// <returns>Normalized angle</returns>
    public static double NormalizeAngle(double angle, double range = 180)
    {
        while (angle > range) angle -= 360;
        while (angle <= -range) angle += 360;
        return angle;
    }

    /// <summary>
    /// Checks if two angles are close within tolerance.
    /// </summary>
    /// <param name="angle1">First angle in degrees</param>
    /// <param name="angle2">Second angle in degrees</param>
    /// <param name="tolerance">Tolerance in degrees</param>
    /// <returns>True if angles differ by less than tolerance</returns>
    public static bool IsAngleClose(double angle1, double angle2, double tolerance = DefaultAngleTolerance)
    {
        return Math.Abs(NormalizeAngle(angle1 - angle2)) < tolerance;
    }

    /// <summary>
    /// Checks if an angle is cardinal (0°, 90°, 180°, or 270°).
    /// </summary>
    /// <param name="angle">Angle in degrees</param>
    /// <param name="tolerance">Tolerance in degrees</param>
    /// <returns>True if angle is close to a cardinal direction</returns>
    public static bool IsCardinal(double angle, double tolerance = DefaultAngleTolerance)
    {
        angle = NormalizeAngle(angle);
        return Math.Abs(angle) < tolerance ||
               Math.Abs(angle - 90) < tolerance ||
               Math.Abs(angle - 180) < tolerance ||
               Math.Abs(angle + 180) < tolerance ||
               Math.Abs(angle - 270) < tolerance ||
               Math.Abs(angle + 90) < tolerance;
    }

    /// <summary>
    /// Checks if an angle is horizontal (0° or 180°).
    /// </summary>
    /// <param name="angle">Angle in degrees</param>
    /// <param name="tolerance">Tolerance in degrees</param>
    /// <returns>True if angle is close to horizontal</returns>
    public static bool IsHorizontal(double angle, double tolerance = DefaultAngleTolerance)
    {
        angle = NormalizeAngle(angle);
        return Math.Abs(angle) < tolerance || Math.Abs(Math.Abs(angle) - 180) < tolerance;
    }

    /// <summary>
    /// Checks if an angle is vertical (90° or 270°).
    /// </summary>
    /// <param name="angle">Angle in degrees</param>
    /// <param name="tolerance">Tolerance in degrees</param>
    /// <returns>True if angle is close to vertical</returns>
    public static bool IsVertical(double angle, double tolerance = DefaultAngleTolerance)
    {
        angle = NormalizeAngle(angle);
        return Math.Abs(angle - 90) < tolerance || Math.Abs(angle + 90) < tolerance;
    }

    /// <summary>
    /// Quantizes an angle to the nearest cardinal direction (0°, 90°, 180°, 270°).
    /// </summary>
    /// <param name="angle">Input angle in degrees</param>
    /// <returns>Nearest cardinal angle</returns>
    public static double QuantizeToCardinal(double angle)
    {
        angle = NormalizeAngle(angle);

        // Symmetric ranges around each cardinal direction
        if (angle >= -45 && angle < 45)
            return 0;    // East
        if (angle >= 45 && angle < 135)
            return 90;   // North
        if (angle >= 135 || angle < -135)
            return 180;  // West
        // angle >= -135 && angle < -45
        return 270;      // South
    }

    /// <summary>
    /// Converts a GridDirection to angle in degrees.
    /// </summary>
    /// <param name="direction">Grid direction</param>
    /// <returns>Angle in degrees (0=East, 90=North, 180=West, 270=South)</returns>
    public static double DirectionToAngle(GridDirection direction)
    {
        return direction switch
        {
            GridDirection.East => 0,
            GridDirection.North => 90,
            GridDirection.West => 180,
            GridDirection.South => 270,
            _ => 0
        };
    }

    /// <summary>
    /// Converts an angle in degrees to a GridDirection.
    /// </summary>
    /// <param name="angle">Angle in degrees</param>
    /// <returns>Nearest grid direction</returns>
    public static GridDirection AngleToDirection(double angle)
    {
        double quantized = QuantizeToCardinal(angle);
        return quantized switch
        {
            0 => GridDirection.East,
            90 => GridDirection.North,
            180 => GridDirection.West,
            270 => GridDirection.South,
            _ => GridDirection.East
        };
    }
}
