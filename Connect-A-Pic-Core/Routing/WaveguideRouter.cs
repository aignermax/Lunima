using CAP_Core.Components;

namespace CAP_Core.Routing;

/// <summary>
/// Routes waveguides between physical pins, generating path segments.
/// </summary>
public class WaveguideRouter
{
    /// <summary>
    /// Minimum bend radius in micrometers. Violating this causes high loss.
    /// </summary>
    public double MinBendRadiusMicrometers { get; set; } = 10.0;

    /// <summary>
    /// Minimum spacing between waveguides in micrometers.
    /// </summary>
    public double MinWaveguideSpacingMicrometers { get; set; } = 2.0;

    /// <summary>
    /// List of obstacles (component bounding boxes) to route around.
    /// </summary>
    public List<RoutingObstacle> Obstacles { get; } = new();

    /// <summary>
    /// Routes a waveguide between two pins.
    /// </summary>
    public RoutedPath Route(PhysicalPin startPin, PhysicalPin endPin)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endAngle = endPin.GetAbsoluteAngle();

        // The end pin's "input" direction is opposite to its defined angle
        double endInputAngle = NormalizeAngle(endAngle + 180);

        var path = new RoutedPath();

        // Try different routing strategies in order of preference
        if (TryRouteStraight(startX, startY, startAngle, endX, endY, endInputAngle, path))
        {
            return path;
        }

        if (TryRouteSBend(startX, startY, startAngle, endX, endY, endInputAngle, path))
        {
            return path;
        }

        // Fall back to manhattan routing with bends
        RouteManhattan(startX, startY, startAngle, endX, endY, endInputAngle, path);
        return path;
    }

    /// <summary>
    /// Attempts to route with a straight line (only works if pins are aligned).
    /// </summary>
    private bool TryRouteStraight(double startX, double startY, double startAngle,
                                   double endX, double endY, double endInputAngle,
                                   RoutedPath path)
    {
        double dx = endX - startX;
        double dy = endY - startY;
        double connectionAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

        // Check if start pin points toward end
        double startDiff = Math.Abs(NormalizeAngle(connectionAngle - startAngle));
        // Check if end pin can receive from this direction
        double endDiff = Math.Abs(NormalizeAngle(connectionAngle - endInputAngle));

        // Allow small tolerance for "straight" routing
        if (startDiff < 5 && endDiff < 5)
        {
            double length = Math.Sqrt(dx * dx + dy * dy);
            path.Segments.Add(new StraightSegment(startX, startY, endX, endY, startAngle));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to route with an S-bend (two opposing bends).
    /// Works when pins are parallel but offset.
    /// </summary>
    private bool TryRouteSBend(double startX, double startY, double startAngle,
                                double endX, double endY, double endInputAngle,
                                RoutedPath path)
    {
        // S-bend works when pins are roughly parallel (same or opposite direction)
        double angleDiff = Math.Abs(NormalizeAngle(startAngle - endInputAngle));
        if (angleDiff > 30 && angleDiff < 150)
        {
            return false; // Angles too different for S-bend
        }

        // Calculate the offset perpendicular to the start direction
        double startRad = startAngle * Math.PI / 180;
        double forwardX = Math.Cos(startRad);
        double forwardY = Math.Sin(startRad);
        double rightX = -forwardY; // perpendicular
        double rightY = forwardX;

        double dx = endX - startX;
        double dy = endY - startY;

        // Project displacement onto forward and lateral directions
        double forwardDist = dx * forwardX + dy * forwardY;
        double lateralDist = dx * rightX + dy * rightY;

        if (forwardDist < MinBendRadiusMicrometers * 2)
        {
            return false; // Not enough forward distance for S-bend
        }

        // S-bend: first bend, straight section, second bend
        double bendRadius = MinBendRadiusMicrometers;
        double sweepAngle = CalculateSBendSweep(forwardDist, Math.Abs(lateralDist), bendRadius);

        if (double.IsNaN(sweepAngle) || Math.Abs(sweepAngle) > 90)
        {
            return false; // S-bend geometry doesn't work
        }

        // Determine bend direction
        double bendDirection = Math.Sign(lateralDist);
        if (bendDirection == 0) bendDirection = 1;

        // First bend
        double bend1CenterX = startX + rightX * bendRadius * bendDirection;
        double bend1CenterY = startY + rightY * bendRadius * bendDirection;
        var bend1 = new BendSegment(bend1CenterX, bend1CenterY, bendRadius,
                                     startAngle, sweepAngle * bendDirection);
        path.Segments.Add(bend1);

        // Straight section in the middle
        double midAngle = startAngle + sweepAngle * bendDirection;
        double midRad = midAngle * Math.PI / 180;
        double midForwardX = Math.Cos(midRad);
        double midForwardY = Math.Sin(midRad);

        double straightLength = forwardDist - 2 * bendRadius * Math.Sin(Math.Abs(sweepAngle) * Math.PI / 180);
        if (straightLength > 0.1)
        {
            double straightStartX = bend1.EndPoint.X;
            double straightStartY = bend1.EndPoint.Y;
            double straightEndX = straightStartX + midForwardX * straightLength;
            double straightEndY = straightStartY + midForwardY * straightLength;
            path.Segments.Add(new StraightSegment(straightStartX, straightStartY,
                                                   straightEndX, straightEndY, midAngle));
        }

        // Second bend (opposite direction to return to original heading)
        var lastSegment = path.Segments.Last();
        double bend2StartX = lastSegment.EndPoint.X;
        double bend2StartY = lastSegment.EndPoint.Y;
        double midRightX = -midForwardY;
        double midRightY = midForwardX;
        double bend2CenterX = bend2StartX - midRightX * bendRadius * bendDirection;
        double bend2CenterY = bend2StartY - midRightY * bendRadius * bendDirection;
        var bend2 = new BendSegment(bend2CenterX, bend2CenterY, bendRadius,
                                     midAngle, -sweepAngle * bendDirection);
        path.Segments.Add(bend2);

        // Final straight to end if needed
        var finalSegment = path.Segments.Last();
        double remainingDist = Math.Sqrt(Math.Pow(endX - finalSegment.EndPoint.X, 2) +
                                          Math.Pow(endY - finalSegment.EndPoint.Y, 2));
        if (remainingDist > 0.1)
        {
            path.Segments.Add(new StraightSegment(finalSegment.EndPoint.X, finalSegment.EndPoint.Y,
                                                   endX, endY, startAngle));
        }

        return true;
    }

    private double CalculateSBendSweep(double forward, double lateral, double radius)
    {
        // For an S-bend: lateral = 2 * R * (1 - cos(sweep))
        // cos(sweep) = 1 - lateral / (2 * R)
        double cosValue = 1 - lateral / (2 * radius);
        if (cosValue < -1 || cosValue > 1) return double.NaN;
        return Math.Acos(cosValue) * 180 / Math.PI;
    }

    /// <summary>
    /// Manhattan routing with rounded corners - always works but may not be optimal.
    /// </summary>
    private void RouteManhattan(double startX, double startY, double startAngle,
                                 double endX, double endY, double endInputAngle,
                                 RoutedPath path)
    {
        double bendRadius = MinBendRadiusMicrometers;
        double currentX = startX;
        double currentY = startY;
        double currentAngle = startAngle;

        // Determine target angle to reach end point
        double dx = endX - currentX;
        double dy = endY - currentY;
        double targetAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

        // First: turn toward the target if needed
        double turnNeeded = NormalizeAngle(targetAngle - currentAngle);

        if (Math.Abs(turnNeeded) > 5)
        {
            // Add a bend
            var (bendCenterX, bendCenterY, sweepAngle) = CalculateBendToAngle(
                currentX, currentY, currentAngle, targetAngle, bendRadius);

            var bend = new BendSegment(bendCenterX, bendCenterY, bendRadius, currentAngle, sweepAngle);
            path.Segments.Add(bend);

            currentX = bend.EndPoint.X;
            currentY = bend.EndPoint.Y;
            currentAngle = bend.EndAngleDegrees;
        }

        // Now go straight toward end (or close to it)
        dx = endX - currentX;
        dy = endY - currentY;
        double distToEnd = Math.Sqrt(dx * dx + dy * dy);

        // If we need to turn again at the end, leave room for the bend
        double finalTurn = NormalizeAngle(endInputAngle - currentAngle);
        double straightDist = distToEnd;

        if (Math.Abs(finalTurn) > 5)
        {
            straightDist = Math.Max(0, distToEnd - bendRadius * 2);
        }

        if (straightDist > 0.1)
        {
            double rad = currentAngle * Math.PI / 180;
            double newX = currentX + Math.Cos(rad) * straightDist;
            double newY = currentY + Math.Sin(rad) * straightDist;
            path.Segments.Add(new StraightSegment(currentX, currentY, newX, newY, currentAngle));
            currentX = newX;
            currentY = newY;
        }

        // Final turn to match end pin direction
        if (Math.Abs(finalTurn) > 5)
        {
            var (bendCenterX, bendCenterY, sweepAngle) = CalculateBendToAngle(
                currentX, currentY, currentAngle, endInputAngle, bendRadius);

            var bend = new BendSegment(bendCenterX, bendCenterY, bendRadius, currentAngle, sweepAngle);
            path.Segments.Add(bend);

            currentX = bend.EndPoint.X;
            currentY = bend.EndPoint.Y;
            currentAngle = bend.EndAngleDegrees;
        }

        // Final straight to exactly reach end point
        dx = endX - currentX;
        dy = endY - currentY;
        double finalDist = Math.Sqrt(dx * dx + dy * dy);
        if (finalDist > 0.1)
        {
            path.Segments.Add(new StraightSegment(currentX, currentY, endX, endY, currentAngle));
        }
    }

    private (double centerX, double centerY, double sweepAngle) CalculateBendToAngle(
        double startX, double startY, double startAngle, double targetAngle, double radius)
    {
        double sweepAngle = NormalizeAngle(targetAngle - startAngle);
        double bendDirection = Math.Sign(sweepAngle);
        if (bendDirection == 0) bendDirection = 1;

        double startRad = startAngle * Math.PI / 180;
        double perpX = -Math.Sin(startRad) * bendDirection;
        double perpY = Math.Cos(startRad) * bendDirection;

        double centerX = startX + perpX * radius;
        double centerY = startY + perpY * radius;

        return (centerX, centerY, sweepAngle);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle <= -180) angle += 360;
        return angle;
    }
}

/// <summary>
/// Represents a routed path consisting of multiple segments.
/// </summary>
public class RoutedPath
{
    public List<PathSegment> Segments { get; } = new();

    /// <summary>
    /// Total length of the path in micrometers.
    /// </summary>
    public double TotalLengthMicrometers => Segments.Sum(s => s.LengthMicrometers);

    /// <summary>
    /// Total equivalent 90-degree bends in the path.
    /// </summary>
    public double TotalEquivalent90DegreeBends => Segments
        .OfType<BendSegment>()
        .Sum(b => b.Equivalent90DegreeBends);

    /// <summary>
    /// Checks if the path is valid (segments connect properly).
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (Segments.Count == 0) return false;
            for (int i = 1; i < Segments.Count; i++)
            {
                var prev = Segments[i - 1];
                var curr = Segments[i];
                double dist = Math.Sqrt(Math.Pow(curr.StartPoint.X - prev.EndPoint.X, 2) +
                                        Math.Pow(curr.StartPoint.Y - prev.EndPoint.Y, 2));
                if (dist > 0.1) return false; // Gap in path
            }
            return true;
        }
    }
}

/// <summary>
/// Represents an obstacle that waveguides must route around.
/// </summary>
public class RoutingObstacle
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Component? SourceComponent { get; set; }

    public bool Contains(double px, double py)
    {
        return px >= X && px <= X + Width && py >= Y && py <= Y + Height;
    }

    public bool Intersects(double x1, double y1, double x2, double y2)
    {
        // Simple line-rectangle intersection check
        // Check if line segment intersects any edge of the rectangle
        return LineIntersectsRect(x1, y1, x2, y2, X, Y, Width, Height);
    }

    private static bool LineIntersectsRect(double x1, double y1, double x2, double y2,
                                            double rx, double ry, double rw, double rh)
    {
        // Check all four edges
        return LineIntersectsLine(x1, y1, x2, y2, rx, ry, rx + rw, ry) ||
               LineIntersectsLine(x1, y1, x2, y2, rx + rw, ry, rx + rw, ry + rh) ||
               LineIntersectsLine(x1, y1, x2, y2, rx, ry + rh, rx + rw, ry + rh) ||
               LineIntersectsLine(x1, y1, x2, y2, rx, ry, rx, ry + rh) ||
               (x1 >= rx && x1 <= rx + rw && y1 >= ry && y1 <= ry + rh); // Start inside
    }

    private static bool LineIntersectsLine(double x1, double y1, double x2, double y2,
                                            double x3, double y3, double x4, double y4)
    {
        double d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(d) < 1e-10) return false;

        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d;
        double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / d;

        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}
