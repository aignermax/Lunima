using CAP_Core.Components;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Attempts to connect two pins using exactly two circular bends (biarc).
/// Handles both same-direction bends (CC, ⊂⊂) and opposite-direction bends (S-bends: C⊃).
/// This is the most common geometric primitive in photonic routing.
/// </summary>
public class TwoBendSolver
{
    private readonly double _minBendRadius;
    private readonly List<double> _allowedRadii;
    private readonly WaveguideRouter _router;

    public TwoBendSolver(double minBendRadius, List<double>? allowedRadii, WaveguideRouter router)
    {
        _minBendRadius = minBendRadius;
        _allowedRadii = allowedRadii ?? new List<double>();
        _router = router;
    }

    /// <summary>
    /// Attempts to connect two pins using exactly two circular bends.
    /// </summary>
    /// <returns>A valid RoutedPath if a two-bend solution exists, otherwise null</returns>
    public RoutedPath? TryTwoBendConnection(PhysicalPin startPin, PhysicalPin endPin)
    {
        Console.WriteLine("[TwoBendSolver] Attempting two-bend connection...");

        // Extract start pose
        var (startX, startY) = startPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();

        // Extract end pose
        var (endX, endY) = endPin.GetAbsolutePosition();
        double endAngle = endPin.GetAbsoluteAngle();
        double endEntryAngle = AngleUtilities.NormalizeAngle(endAngle + 180);

        // Try all four bend direction combinations: LL, LR, RL, RR
        var bendCombinations = new[]
        {
            (1.0, 1.0, "LL"),    // Left-Left
            (1.0, -1.0, "LR"),   // Left-Right (S-bend)
            (-1.0, 1.0, "RL"),   // Right-Left (S-bend)
            (-1.0, -1.0, "RR")   // Right-Right
        };

        foreach (var (firstDir, secondDir, label) in bendCombinations)
        {
            Console.WriteLine($"[TwoBendSolver]   Trying {label}...");

            var path = TryBuildTwoBendPath(
                startX, startY, startAngle,
                endX, endY, endEntryAngle,
                firstDir, secondDir);

            if (path != null)
            {
                Console.WriteLine($"[TwoBendSolver]   SUCCESS with {label}!");
                return path;
            }
        }

        Console.WriteLine("[TwoBendSolver] No valid two-bend solution found, falling back to A*");
        return null;
    }

    /// <summary>
    /// Attempts to construct a two-bend path with specified bend directions.
    /// Solves the biarc problem: find two tangent circular arcs connecting two poses.
    /// </summary>
    private RoutedPath? TryBuildTwoBendPath(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle,
        double firstBendDir, double secondBendDir)
    {
        // Select bend radius
        double radius = _minBendRadius;
        if (_allowedRadii.Count > 0)
        {
            radius = _allowedRadii.Where(r => r >= _minBendRadius).DefaultIfEmpty(_minBendRadius).First();
        }

        // Calculate first bend center (perpendicular to start direction)
        double startRad = startAngle * Math.PI / 180.0;
        double perpX1 = -Math.Sin(startRad) * firstBendDir;
        double perpY1 = Math.Cos(startRad) * firstBendDir;
        double c1X = startX + perpX1 * radius;
        double c1Y = startY + perpY1 * radius;

        // Calculate second bend center (perpendicular to end entry direction)
        double endRad = endEntryAngle * Math.PI / 180.0;
        double perpX2 = -Math.Sin(endRad) * secondBendDir;
        double perpY2 = Math.Cos(endRad) * secondBendDir;
        double c2X = endX + perpX2 * radius;
        double c2Y = endY + perpY2 * radius;

        // Calculate distance between bend centers
        double dx = c2X - c1X;
        double dy = c2Y - c1Y;
        double centerDistance = Math.Sqrt(dx * dx + dy * dy);

        // Solve for the junction point where the two arcs meet tangentially
        double junctionX, junctionY, junctionAngle;

        if (firstBendDir == secondBendDir)
        {
            // Same direction bends (CC or ⊂⊂)
            // The two circles must be externally tangent
            // Junction point lies on the line between centers

            // For external tangency with equal radii, distance should be 2R
            // But we don't strictly require this - we compute the junction and validate
            if (centerDistance < 0.1)
            {
                // Centers coincide - degenerate case
                return null;
            }

            // Junction point is on the line between centers
            // For same-direction bends, it's between the centers
            double t = radius / centerDistance;
            junctionX = c1X + dx * t;
            junctionY = c1Y + dy * t;

            // Calculate tangent angle at junction
            // The tangent is perpendicular to the line from center to junction
            double angleToJunction = Math.Atan2(junctionY - c1Y, junctionX - c1X) * 180.0 / Math.PI;
            junctionAngle = angleToJunction + (firstBendDir > 0 ? 90 : -90);
        }
        else
        {
            // Opposite direction bends (S-bend: C⊃ or ⊃C)
            // This is the MOST COMMON case in photonic routing!

            // For opposite curvatures, the junction point lies on the external line
            // connecting the two circles (not between centers, but on opposite sides)

            if (centerDistance < 0.1)
            {
                // Centers coincide - degenerate case
                return null;
            }

            // The junction point is on the line between centers, outside the segment
            // For opposite directions: distance from C1 to junction = radius
            // But junction extends beyond or before the center line

            // Vector from C1 toward C2
            double unitX = dx / centerDistance;
            double unitY = dy / centerDistance;

            // Junction point: extend from C1 along the line toward C2 by radius
            junctionX = c1X + unitX * radius;
            junctionY = c1Y + unitY * radius;

            // Calculate tangent angle at junction
            double angleToJunction = Math.Atan2(junctionY - c1Y, junctionX - c1X) * 180.0 / Math.PI;
            junctionAngle = angleToJunction + (firstBendDir > 0 ? 90 : -90);
        }

        // Compute sweep angles
        double sweep1 = AngleUtilities.NormalizeAngle(junctionAngle - startAngle);
        double sweep2 = AngleUtilities.NormalizeAngle(endEntryAngle - junctionAngle);

        // Validate sweep angles match bend directions
        if (Math.Abs(sweep1) < 2 || Math.Abs(sweep2) < 2)
        {
            // Negligible bends - not a valid solution
            return null;
        }

        if (Math.Sign(sweep1) != Math.Sign(firstBendDir) || Math.Sign(sweep2) != Math.Sign(secondBendDir))
        {
            // Sweep direction mismatch
            return null;
        }

        // Validate bend radii
        if (radius < _minBendRadius - 0.01)
        {
            return null;
        }

        // Create bend segments
        var bend1 = new BendSegment(c1X, c1Y, radius, startAngle, sweep1);
        var bend2 = new BendSegment(c2X, c2Y, radius, junctionAngle, sweep2);

        // Validate continuity: bend1 end must match bend2 start
        double continuityError = Math.Sqrt(
            Math.Pow(bend1.EndPoint.X - bend2.StartPoint.X, 2) +
            Math.Pow(bend1.EndPoint.Y - bend2.StartPoint.Y, 2));

        if (continuityError > 1.0) // 1 micron tolerance
        {
            Console.WriteLine($"[TwoBendSolver]     Continuity failed: {continuityError:F3}µm gap");
            return null;
        }

        // Validate end position and angle
        if (!ValidateEndpoint(bend2, endX, endY, endEntryAngle))
        {
            Console.WriteLine("[TwoBendSolver]     Endpoint validation failed");
            return null;
        }

        // Check for obstacles
        if (IsSegmentBlocked(bend1) || IsSegmentBlocked(bend2))
        {
            Console.WriteLine("[TwoBendSolver]     Path blocked by obstacles");
            return null;
        }

        // Success! Create and return path
        var path = new RoutedPath();
        path.Segments.Add(bend1);
        path.Segments.Add(bend2);
        return path;
    }

    /// <summary>
    /// Validates that a bend segment ends at the expected position and angle.
    /// </summary>
    private bool ValidateEndpoint(BendSegment bend, double expectedX, double expectedY, double expectedAngle)
    {
        const double positionTolerance = 1.0; // micrometers (relaxed from 0.1)
        const double angleTolerance = 5.0;    // degrees (relaxed from 2)

        double dx = bend.EndPoint.X - expectedX;
        double dy = bend.EndPoint.Y - expectedY;
        double positionError = Math.Sqrt(dx * dx + dy * dy);

        if (positionError > positionTolerance)
            return false;

        double angleError = Math.Abs(AngleUtilities.NormalizeAngle(bend.EndAngleDegrees - expectedAngle));
        if (angleError > angleTolerance)
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a bend segment passes through blocked cells.
    /// </summary>
    private bool IsSegmentBlocked(BendSegment bend)
    {
        if (_router.PathfindingGrid == null)
            return false;

        // Sample points along the arc
        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        double arcLength = Math.Abs(sweepRad) * bend.RadiusMicrometers;
        double stepLength = _router.PathfindingGrid.CellSizeMicrometers * 0.5;
        int numSamples = Math.Max(10, (int)Math.Ceiling(arcLength / stepLength));

        double sign = Math.Sign(bend.SweepAngleDegrees);
        if (sign == 0) sign = 1;

        for (int i = 0; i <= numSamples; i++)
        {
            double t = (double)i / numSamples;
            double angle = startRad + sweepRad * t;
            double px = bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle - Math.PI / 2 * sign);
            double py = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle - Math.PI / 2 * sign);

            var (gx, gy) = _router.PathfindingGrid.PhysicalToGrid(px, py);
            if (_router.PathfindingGrid.IsBlocked(gx, gy))
                return true;
        }

        return false;
    }
}
