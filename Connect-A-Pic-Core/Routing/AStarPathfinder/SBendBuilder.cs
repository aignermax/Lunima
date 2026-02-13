using CAP_Core.Routing;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Builds S-bend patterns for path corrections and non-cardinal approaches.
/// Consolidates duplicate S-bend logic from PathSmoother.
/// </summary>
public class SBendBuilder
{
    private readonly BendBuilder _bendBuilder;
    private readonly double _minBendRadius;

    /// <summary>
    /// Threshold for lateral offset correction (micrometers).
    /// Only insert correction S-bend if deviation exceeds this.
    /// </summary>
    public double CorrectionThresholdMicrometers { get; set; } = 5.0;

    public SBendBuilder(BendBuilder bendBuilder, double minBendRadius)
    {
        _bendBuilder = bendBuilder;
        _minBendRadius = minBendRadius;
    }

    /// <summary>
    /// Attempts to insert a correction S-bend to bring waveguide back to A* path.
    /// Returns true if S-bend was inserted.
    /// </summary>
    /// <param name="path">Routed path to append to</param>
    /// <param name="x">Current X position (updated)</param>
    /// <param name="y">Current Y position (updated)</param>
    /// <param name="angle">Current angle (updated)</param>
    /// <param name="lateralOffset">Distance from A* path (micrometers)</param>
    /// <param name="correctionAngle">Perpendicular angle toward path</param>
    /// <param name="travelAngle">Desired travel direction after correction</param>
    /// <param name="targetX">Destination X</param>
    /// <param name="targetY">Destination Y</param>
    /// <returns>True if correction S-bend was inserted</returns>
    public bool TryBuildCorrectionSBend(
        RoutedPath path, ref double x, ref double y, ref double angle,
        double lateralOffset, double correctionAngle, double travelAngle,
        double targetX, double targetY)
    {
        // Only correct if offset is significant
        if (lateralOffset < CorrectionThresholdMicrometers)
            return false;

        // Check if we have enough space for S-bend (need room for 2 bends)
        double minSBendLength = _minBendRadius * 4;
        double distanceToTarget = Math.Sqrt(Math.Pow(targetX - x, 2) + Math.Pow(targetY - y, 2));

        if (distanceToTarget < minSBendLength * 1.5)
            return false; // Not enough room

        // Calculate S-bend parameters
        // The S-bend should gently shift the waveguide toward the A* path
        double lateralShift = Math.Min(lateralOffset * 0.7, _minBendRadius * 0.8); // Conservative shift
        double bendAngle = Math.Atan2(lateralShift, _minBendRadius * 2) * 180 / Math.PI;
        bendAngle = Math.Clamp(bendAngle, -45, 45); // Limit to gentle bends

        // Determine bend direction (toward path)
        double angleDiffToCorrection = AngleUtilities.NormalizeAngle(correctionAngle - angle);
        double bendSign = Math.Sign(angleDiffToCorrection);

        // First bend: slightly toward path
        double bend1Angle = angle + bendAngle * bendSign;
        var bend1 = _bendBuilder.BuildBend(x, y, angle, bend1Angle, BendMode.Limited45);
        if (bend1 != null)
        {
            path.Segments.Add(bend1);
            x = bend1.EndPoint.X;
            y = bend1.EndPoint.Y;
            angle = bend1Angle;
        }

        // Straight segment (travel while offset)
        double straightDist = Math.Min(_minBendRadius * 3, distanceToTarget * 0.4);
        double straightAngleRad = angle * Math.PI / 180;
        double straightEndX = x + straightDist * Math.Cos(straightAngleRad);
        double straightEndY = y + straightDist * Math.Sin(straightAngleRad);
        path.Segments.Add(new StraightSegment(x, y, straightEndX, straightEndY, angle));
        x = straightEndX;
        y = straightEndY;

        // Second bend: back to original direction (completing S-bend)
        var bend2 = _bendBuilder.BuildBend(x, y, angle, travelAngle, BendMode.Limited45);
        if (bend2 != null)
        {
            path.Segments.Add(bend2);
            x = bend2.EndPoint.X;
            y = bend2.EndPoint.Y;
            angle = travelAngle;
        }

        return true;
    }

    /// <summary>
    /// Builds an approach S-bend for non-cardinal pin angles.
    /// Creates two bends with a straight segment to reach end position at target angle.
    /// </summary>
    /// <param name="path">Routed path to append to</param>
    /// <param name="x">Current X position (updated)</param>
    /// <param name="y">Current Y position (updated)</param>
    /// <param name="angle">Current angle (updated)</param>
    /// <param name="endX">Target end X position</param>
    /// <param name="endY">Target end Y position</param>
    /// <param name="targetAngle">Target arrival angle</param>
    /// <returns>True if S-bend was successfully built</returns>
    public bool TryBuildApproachSBend(
        RoutedPath path, ref double x, ref double y, ref double angle,
        double endX, double endY, double targetAngle)
    {
        double dx = endX - x;
        double dy = endY - y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        // Need minimum distance for S-bend
        if (distance < _minBendRadius * 3)
            return false;

        // Calculate the angle to reach end point directly from current position
        double directAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

        double currentToTargetTurn = AngleUtilities.NormalizeAngle(targetAngle - angle);
        double currentToDirectTurn = AngleUtilities.NormalizeAngle(directAngle - angle);

        // Determine if we need complex S-bend or simple approach
        bool needSBend = Math.Abs(currentToDirectTurn - currentToTargetTurn) > 15;

        if (!needSBend)
        {
            // Simple case: turn to final angle, then go straight
            if (Math.Abs(currentToTargetTurn) > 5)
            {
                var turnBend = _bendBuilder.BuildBend(x, y, angle, targetAngle, BendMode.Flexible);
                if (turnBend != null)
                {
                    path.Segments.Add(turnBend);
                    x = turnBend.EndPoint.X;
                    y = turnBend.EndPoint.Y;
                    angle = targetAngle;
                }
            }

            // Go straight to end
            if (Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2)) > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, angle));
                x = endX;
                y = endY;
            }

            return true;
        }

        // S-bend: turn toward midpoint direction, go straight, turn to final angle
        double midAngle = AngleUtilities.NormalizeAngle((directAngle + targetAngle) / 2);
        double turn1 = AngleUtilities.NormalizeAngle(midAngle - angle);
        double turn2 = AngleUtilities.NormalizeAngle(targetAngle - midAngle);

        // Limit turns to reasonable values
        if (Math.Abs(turn1) > 90) turn1 = Math.Sign(turn1) * 90;
        if (Math.Abs(turn2) > 90) turn2 = Math.Sign(turn2) * 90;

        // First bend
        if (Math.Abs(turn1) > 5)
        {
            var bend1 = _bendBuilder.BuildBend(x, y, angle, angle + turn1, BendMode.Flexible);
            if (bend1 != null)
            {
                path.Segments.Add(bend1);
                x = bend1.EndPoint.X;
                y = bend1.EndPoint.Y;
                angle = AngleUtilities.NormalizeAngle(angle + turn1);
            }
        }

        // Calculate how far we can go before the final bend
        double remainingDist = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
        double bendSpace = _minBendRadius;
        double straightDist = Math.Max(0, remainingDist - bendSpace);

        // Go straight if there's room
        if (straightDist > 0.5)
        {
            double straightAngleRad = angle * Math.PI / 180;
            double newX = x + straightDist * Math.Cos(straightAngleRad);
            double newY = y + straightDist * Math.Sin(straightAngleRad);
            path.Segments.Add(new StraightSegment(x, y, newX, newY, angle));
            x = newX;
            y = newY;
        }

        // Final bend to target angle
        if (Math.Abs(turn2) > 5)
        {
            var bend2 = _bendBuilder.BuildBend(x, y, angle, targetAngle, BendMode.Flexible);
            if (bend2 != null)
            {
                path.Segments.Add(bend2);
                x = bend2.EndPoint.X;
                y = bend2.EndPoint.Y;
                angle = targetAngle;
            }
        }

        // Final straight to exact end position
        if (Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2)) > 0.5)
        {
            path.Segments.Add(new StraightSegment(x, y, endX, endY, angle));
            x = endX;
            y = endY;
        }

        return true;
    }
}
