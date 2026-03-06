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
        Console.WriteLine($"[TerminalConnector] Connecting from ({x:F1},{y:F1}) @ {currentAngle}° to ({endX:F1},{endY:F1}) @ {endEntryAngle}°");

        double dx = endX - x;
        double dy = endY - y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        Console.WriteLine($"[TerminalConnector] Distance to end: {distance:F2}µm");

        if (distance < 0.1)
        {
            Console.WriteLine($"[TerminalConnector] Already at pin");
            return true; // Already at pin
        }

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
            Console.WriteLine($"[TerminalConnector] STRATEGY 1: Already on axis");
            if (!IsLineAlignedWithAngle(x, y, endX, endY, endEntryAngle))
            {
                Console.WriteLine($"[TerminalConnector] STRATEGY 1 FAILED: Line not aligned with angle");
                return false;
            }

            if (forwardDistance > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }
            Console.WriteLine($"[TerminalConnector] STRATEGY 1 SUCCESS");
            return true;
        }

        // STRATEGY 2: Reach entry axis, then straight to pin
        Console.WriteLine($"[TerminalConnector] STRATEGY 2: Trying to reach entry axis at ({entryAxisX:F1},{entryAxisY:F1})");
        Console.WriteLine($"[TerminalConnector] lateralOffset={lateralOffset:F2}, forwardDistance={forwardDistance:F2}");
        if (TryReachEntryAxis(path, ref x, ref y, ref currentAngle,
            entryAxisX, entryAxisY, endX, endY, endEntryAngle, lateralOffset, forwardDistance))
        {
            Console.WriteLine($"[TerminalConnector] STRATEGY 2: Reached entry axis at ({x:F1},{y:F1})");
            if (Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2)) > 0.5)
            {
                if (!IsLineAlignedWithAngle(x, y, endX, endY, endEntryAngle))
                {
                    Console.WriteLine($"[TerminalConnector] STRATEGY 2 FAILED: Final segment not aligned");
                    return false;
                }

                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }
            Console.WriteLine($"[TerminalConnector] STRATEGY 2 SUCCESS");
            return true;
        }
        Console.WriteLine($"[TerminalConnector] STRATEGY 2 FAILED: Could not reach entry axis");

        // STRATEGY 3: S-bend if very tight
        Console.WriteLine($"[TerminalConnector] STRATEGY 3: Trying S-bend (distance={distance:F2}, threshold={_minBendRadius * 4:F2})");
        if (distance < _minBendRadius * 4)
        {
            bool sBendSuccess = _sBendBuilder.TryBuildApproachSBend(
                path, ref x, ref y, ref currentAngle, endX, endY, endEntryAngle);

            if (sBendSuccess)
            {
                double finalDist = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
                if (finalDist < 0.5 && AngleUtilities.IsAngleClose(currentAngle, endEntryAngle))
                {
                    Console.WriteLine($"[TerminalConnector] STRATEGY 3 SUCCESS");
                    return true;
                }
                Console.WriteLine($"[TerminalConnector] STRATEGY 3: S-bend built but didn't reach target (dist={finalDist:F2})");
            }
            Console.WriteLine($"[TerminalConnector] STRATEGY 3 FAILED: S-bend failed");
        }
        else
        {
            Console.WriteLine($"[TerminalConnector] STRATEGY 3 SKIPPED: distance too large");
        }

        // STRATEGY 4: Close-range turn + straight (for perpendicular final approaches)
        Console.WriteLine($"[TerminalConnector] STRATEGY 4: Trying close-range turn + straight");
        double angleDiff = Math.Abs(AngleUtilities.NormalizeAngle(endEntryAngle - currentAngle));
        if (distance < _minBendRadius * 2 && angleDiff > 45 && angleDiff < 135)
        {
            // Perpendicular or near-perpendicular approach with limited space
            // Use tightest possible bend radius (minBendRadius / 2 if very tight)
            double tightRadius = distance < _minBendRadius ? _minBendRadius * 0.5 : _minBendRadius;
            Console.WriteLine($"[TerminalConnector] Using tight radius={tightRadius:F1}µm for close-range turn");

            var finalBend = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible, tightRadius);
            if (finalBend != null)
            {
                path.Segments.Add(finalBend);
                x = finalBend.EndPoint.X;
                y = finalBend.EndPoint.Y;
                currentAngle = endEntryAngle;

                double remainingDist = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
                Console.WriteLine($"[TerminalConnector] After bend: at ({x:F2},{y:F2}), {remainingDist:F2}µm from target");

                if (remainingDist > 0.1)
                {
                    // Calculate actual angle from current position to target
                    double actualAngle = Math.Atan2(endY - y, endX - x) * 180 / Math.PI;
                    actualAngle = AngleUtilities.NormalizeAngle(actualAngle);

                    double angleError = Math.Abs(AngleUtilities.NormalizeAngle(actualAngle - endEntryAngle));
                    Console.WriteLine($"[TerminalConnector] Final segment: declared={endEntryAngle:F1}° actual={actualAngle:F1}° error={angleError:F1}°");

                    // Only accept if angle error is small (G1 continuity requirement)
                    if (angleError > 5)
                    {
                        Console.WriteLine($"[TerminalConnector] STRATEGY 4 FAILED: Angle error too large ({angleError:F1}°)");
                        return false;
                    }

                    // Use actual angle for the segment
                    path.Segments.Add(new StraightSegment(x, y, endX, endY, actualAngle));
                    x = endX;
                    y = endY;
                    currentAngle = actualAngle;
                }

                Console.WriteLine($"[TerminalConnector] STRATEGY 4 SUCCESS");
                return true;
            }
        }

        // STRATEGY 5: Tiny S-bend for very close same-direction pins with lateral offset
        if (distance < _minBendRadius && Math.Abs(AngleUtilities.NormalizeAngle(endEntryAngle - currentAngle)) < 10)
        {
            Console.WriteLine($"[TerminalConnector] STRATEGY 5: Trying tiny S-bend for close same-direction pins");
            // Use half the minimum radius for these extremely tight cases
            double tinyRadius = _minBendRadius * 0.5;
            double lateralShift = Math.Abs(lateralOffset);

            if (lateralShift > 0.5 && forwardDistance > tinyRadius)
            {
                // Calculate bend angle for the lateral shift
                double bendAngle = Math.Atan2(lateralShift, forwardDistance) * 180 / Math.PI;
                bendAngle = Math.Clamp(bendAngle, 15, 60);
                double bendSign = Math.Sign(lateralOffset);

                // First bend (toward offset direction)
                double midAngle = AngleUtilities.NormalizeAngle(currentAngle + bendAngle * bendSign);
                var bend1 = _bendBuilder.BuildBend(x, y, currentAngle, midAngle, BendMode.Flexible, tinyRadius);
                if (bend1 != null)
                {
                    path.Segments.Add(bend1);
                    x = bend1.EndPoint.X;
                    y = bend1.EndPoint.Y;
                    currentAngle = midAngle;

                    // Calculate remaining distance and angle to target
                    double remainingDx = endX - x;
                    double remainingDy = endY - y;
                    double remainingDist = Math.Sqrt(remainingDx * remainingDx + remainingDy * remainingDy);

                    if (remainingDist > 0.5)
                    {
                        // Add short straight if there's room
                        double straightDist = Math.Min(remainingDist * 0.4, tinyRadius);
                        if (straightDist > 0.5)
                        {
                            double angleRad = currentAngle * Math.PI / 180;
                            double straightEndX = x + straightDist * Math.Cos(angleRad);
                            double straightEndY = y + straightDist * Math.Sin(angleRad);
                            path.Segments.Add(new StraightSegment(x, y, straightEndX, straightEndY, currentAngle));
                            x = straightEndX;
                            y = straightEndY;
                        }

                        // Second bend (back to target angle)
                        var bend2 = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible, tinyRadius);
                        if (bend2 != null)
                        {
                            path.Segments.Add(bend2);
                            x = bend2.EndPoint.X;
                            y = bend2.EndPoint.Y;
                            currentAngle = endEntryAngle;
                        }

                        // Final straight to exact target
                        double finalDist = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
                        if (finalDist > 0.1)
                        {
                            double finalAngle = Math.Atan2(endY - y, endX - x) * 180 / Math.PI;
                            double angleError = Math.Abs(AngleUtilities.NormalizeAngle(finalAngle - endEntryAngle));

                            // Check G1 continuity (bend should end aligned with target direction)
                            if (angleError > 5)
                            {
                                Console.WriteLine($"[TerminalConnector] STRATEGY 5 FAILED: Final angle error {angleError:F1}°");
                                return false;
                            }

                            path.Segments.Add(new StraightSegment(x, y, endX, endY, finalAngle));
                            x = endX;
                            y = endY;
                        }

                        Console.WriteLine($"[TerminalConnector] STRATEGY 5 SUCCESS");
                        return true;
                    }
                }
            }
        }

        Console.WriteLine($"[TerminalConnector] ALL STRATEGIES FAILED - cannot solve geometry");
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
