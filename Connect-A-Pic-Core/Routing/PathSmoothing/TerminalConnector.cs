using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.SegmentBuilders;
using CAP_Core.Routing.Utilities;

namespace CAP_Core.Routing.PathSmoothing;

/// <summary>
/// Handles terminal approach - geometrically connecting path to end pin.
/// INVARIANT: Final segment MUST be aligned with endEntryAngle.
/// </summary>
public class TerminalConnector
{
    private readonly double _minBendRadius;
    private readonly BendBuilder _bendBuilder;
    private readonly SBendBuilder _sBendBuilder;

    public TerminalConnector(double minBendRadius, BendBuilder bendBuilder, SBendBuilder sBendBuilder)
    {
        _minBendRadius = minBendRadius;
        _bendBuilder = bendBuilder;
        _sBendBuilder = sBendBuilder;
    }

    /// <summary>
    /// Geometrically connects to end pin.
    /// Returns false if valid geometry cannot be constructed.
    /// </summary>
    public bool AppendTerminalApproach(
        RoutedPath path,
        ref double x,
        ref double y,
        ref double currentAngle,
        double endX,
        double endY,
        double endEntryAngle)
    {
        double dx = endX - x;
        double dy = endY - y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 0.1)
            return true; // Already at pin

        // Calculate position on entry axis
        double entryAxisX, entryAxisY;
        if (endEntryAngle == 0 || endEntryAngle == 180)
        {
            entryAxisX = x;
            entryAxisY = endY;
        }
        else
        {
            entryAxisX = endX;
            entryAxisY = y;
        }

        double lateralOffset = CalculateLateralOffset(x, y, currentAngle, endX, endY);
        double forwardDistance = CalculateForwardDistance(x, y, currentAngle, endX, endY);

        // STRATEGY 1: Already on entry axis with correct angle
        if (Math.Abs(lateralOffset) < 0.5 && AngleUtilities.IsAngleClose(currentAngle, endEntryAngle))
        {
            if (!IsLineAlignedWithAngle(x, y, endX, endY, endEntryAngle))
                return false;

            if (forwardDistance > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }
            return true;
        }

        // STRATEGY 2: Reach entry axis, then straight to pin
        if (TryReachEntryAxis(path, ref x, ref y, ref currentAngle,
            entryAxisX, entryAxisY, endX, endY, endEntryAngle, lateralOffset, forwardDistance))
        {
            if (Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2)) > 0.5)
            {
                if (!IsLineAlignedWithAngle(x, y, endX, endY, endEntryAngle))
                    return false;

                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }
            return true;
        }

        // STRATEGY 3: S-bend if very tight
        if (distance < _minBendRadius * 4)
        {
            bool sBendSuccess = _sBendBuilder.TryBuildApproachSBend(
                path, ref x, ref y, ref currentAngle, endX, endY, endEntryAngle);

            if (sBendSuccess)
            {
                double finalDist = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
                if (finalDist < 0.5 && AngleUtilities.IsAngleClose(currentAngle, endEntryAngle))
                    return true;
            }
        }

        return false; // Cannot solve geometry
    }

    private bool TryReachEntryAxis(
        RoutedPath path,
        ref double x,
        ref double y,
        ref double currentAngle,
        double entryAxisX,
        double entryAxisY,
        double endX,
        double endY,
        double endEntryAngle,
        double lateralOffset,
        double forwardDistance)
    {
        if (forwardDistance < _minBendRadius * 1.5)
            return false;

        if (Math.Abs(lateralOffset) > 0.5)
        {
            // Two-bend approach
            double bendAngle = Math.Atan(Math.Abs(lateralOffset) / (forwardDistance * 0.5)) * 180 / Math.PI;
            bendAngle = Math.Clamp(bendAngle, 5, 45);
            double bendSign = Math.Sign(lateralOffset);

            // First bend
            double firstTarget = currentAngle + bendAngle * bendSign;
            var bend1 = _bendBuilder.BuildBend(x, y, currentAngle, firstTarget, BendMode.Flexible);
            if (bend1 == null) return false;

            path.Segments.Add(bend1);
            x = bend1.EndPoint.X;
            y = bend1.EndPoint.Y;
            currentAngle = firstTarget;

            // Straight section
            double straightDist = forwardDistance * 0.4;
            if (straightDist > 0.5)
            {
                double angleRad = currentAngle * Math.PI / 180;
                double straightEndX = x + straightDist * Math.Cos(angleRad);
                double straightEndY = y + straightDist * Math.Sin(angleRad);

                if (!IsLineAlignedWithAngle(x, y, straightEndX, straightEndY, currentAngle))
                    return false;

                path.Segments.Add(new StraightSegment(x, y, straightEndX, straightEndY, currentAngle));
                x = straightEndX;
                y = straightEndY;
            }

            // Second bend
            var bend2 = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible);
            if (bend2 == null) return false;

            path.Segments.Add(bend2);
            x = bend2.EndPoint.X;
            y = bend2.EndPoint.Y;
            currentAngle = endEntryAngle;

            // Straight to entry axis
            double toAxisX = (endEntryAngle == 0 || endEntryAngle == 180) ? x : endX;
            double toAxisY = (endEntryAngle == 0 || endEntryAngle == 180) ? endY : y;

            double distToAxis = Math.Sqrt(Math.Pow(toAxisX - x, 2) + Math.Pow(toAxisY - y, 2));
            if (distToAxis > 0.5)
            {
                if (!IsLineAlignedWithAngle(x, y, toAxisX, toAxisY, currentAngle))
                    return false;

                path.Segments.Add(new StraightSegment(x, y, toAxisX, toAxisY, currentAngle));
                x = toAxisX;
                y = toAxisY;
            }

            return true;
        }
        else
        {
            // Single bend
            double angleDiff = AngleUtilities.NormalizeAngle(endEntryAngle - currentAngle);
            if (Math.Abs(angleDiff) > 5)
            {
                double straightBeforeBend = forwardDistance - _minBendRadius;
                if (straightBeforeBend > 0.5)
                {
                    double angleRad = currentAngle * Math.PI / 180;
                    double straightEndX = x + straightBeforeBend * Math.Cos(angleRad);
                    double straightEndY = y + straightBeforeBend * Math.Sin(angleRad);

                    if (!IsLineAlignedWithAngle(x, y, straightEndX, straightEndY, currentAngle))
                        return false;

                    path.Segments.Add(new StraightSegment(x, y, straightEndX, straightEndY, currentAngle));
                    x = straightEndX;
                    y = straightEndY;
                }

                var bend = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible);
                if (bend == null) return false;

                path.Segments.Add(bend);
                x = bend.EndPoint.X;
                y = bend.EndPoint.Y;
                currentAngle = endEntryAngle;
            }

            return true;
        }
    }

    private static bool IsLineAlignedWithAngle(double x1, double y1, double x2, double y2, double declaredAngle)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 0.1)
            return true;

        double actualAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
        double angleDiff = Math.Abs(AngleUtilities.NormalizeAngle(actualAngle - declaredAngle));

        return angleDiff < 2.0; // 2° tolerance
    }

    private static double CalculateLateralOffset(double x, double y, double currentAngle, double targetX, double targetY)
    {
        double dx = targetX - x;
        double dy = targetY - y;
        double angleRad = currentAngle * Math.PI / 180;
        double perpX = -Math.Sin(angleRad);
        double perpY = Math.Cos(angleRad);
        return dx * perpX + dy * perpY;
    }

    private static double CalculateForwardDistance(double x, double y, double currentAngle, double targetX, double targetY)
    {
        double dx = targetX - x;
        double dy = targetY - y;
        double angleRad = currentAngle * Math.PI / 180;
        return dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
    }
}
