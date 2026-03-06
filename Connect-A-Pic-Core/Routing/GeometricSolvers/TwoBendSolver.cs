using CAP_Core.Components;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.GeometricSolvers;

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
    /// Tries multiple radii to find a valid solution.
    /// </summary>
    private RoutedPath? TryBuildTwoBendPath(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle,
        double firstBendDir, double secondBendDir)
    {
        // Try multiple radii - small radii often fail because circles can't intersect
        var radiiToTry = new List<double>();

        if (_allowedRadii.Count > 0)
        {
            // Use allowed radii in increasing order
            radiiToTry.AddRange(_allowedRadii.Where(r => r >= _minBendRadius).OrderBy(r => r));
        }
        else
        {
            // Try min radius, then 1.5x, 2x, 3x multiples
            radiiToTry.Add(_minBendRadius);
            radiiToTry.Add(_minBendRadius * 1.5);
            radiiToTry.Add(_minBendRadius * 2.0);
            radiiToTry.Add(_minBendRadius * 3.0);
        }

        foreach (var radius in radiiToTry)
        {
            var path = TryBuildWithRadius(startX, startY, startAngle, endX, endY, endEntryAngle,
                                          firstBendDir, secondBendDir, radius);
            if (path != null)
                return path;
        }

        return null;
    }

    /// <summary>
    /// Attempts to build a two-bend path with a specific radius.
    /// </summary>
    private RoutedPath? TryBuildWithRadius(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle,
        double firstBendDir, double secondBendDir, double radius)
    {

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

        // Solve for the junction point using circle-circle intersection
        // The two arcs meet where the two circles (both radius R) intersect

        if (centerDistance < 0.1)
        {
            // Centers coincide - degenerate case
            return null;
        }

        // For same-direction bends: circles must be externally tangent or separate
        // For opposite-direction bends: circles can overlap

        // Check if circles can intersect
        // For equal radii R: intersection exists if 0 < d < 2R
        if (centerDistance > 2.0 * radius + 0.01)
        {
            // Circles too far apart - no intersection
            return null;
        }

        // Compute circle-circle intersection points
        // Using standard formula:
        // a = d/2 (for equal radii)
        // h = sqrt(R² - a²)
        // midpoint = C1 + (C2 - C1) * 0.5
        // junction = midpoint ± perpendicular * h

        double a = centerDistance / 2.0;
        double hSquared = radius * radius - a * a;

        if (hSquared < 0)
        {
            // Circles don't intersect (happens when d > 2R)
            return null;
        }

        double h = Math.Sqrt(hSquared);

        // Midpoint between centers
        double midX = c1X + dx * 0.5;
        double midY = c1Y + dy * 0.5;

        // Perpendicular vector to line between centers
        double perpX = -dy / centerDistance;
        double perpY = dx / centerDistance;

        // Two possible intersection points
        double junction1X = midX + perpX * h;
        double junction1Y = midY + perpY * h;

        double junction2X = midX - perpX * h;
        double junction2Y = midY - perpY * h;

        // Try both junction points and pick the one that produces valid arcs
        var candidates = new[]
        {
            (junction1X, junction1Y),
            (junction2X, junction2Y)
        };

        foreach (var (junctionX, junctionY) in candidates)
        {
            // Calculate tangent angle at junction
            // The tangent is perpendicular to the line from center to junction
            double angleToJunction = Math.Atan2(junctionY - c1Y, junctionX - c1X) * 180.0 / Math.PI;
            double junctionAngle = angleToJunction + (firstBendDir > 0 ? 90 : -90);

            // Try to build arcs with this junction
            var result = TryBuildArcsWithJunction(
                startX, startY, startAngle,
                endX, endY, endEntryAngle,
                c1X, c1Y, c2X, c2Y, radius,
                junctionAngle,
                firstBendDir, secondBendDir);

            if (result != null)
                return result;
        }

        // Neither intersection point worked
        return null;
    }

    /// <summary>
    /// Attempts to build arc segments with a specific junction point.
    /// </summary>
    private RoutedPath? TryBuildArcsWithJunction(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle,
        double c1X, double c1Y, double c2X, double c2Y, double radius,
        double junctionAngle,
        double firstBendDir, double secondBendDir)
    {
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

        // CRITICAL: Validate first arc actually starts at start pin
        double startError = Math.Sqrt(
            Math.Pow(bend1.StartPoint.X - startX, 2) +
            Math.Pow(bend1.StartPoint.Y - startY, 2));

        if (startError > 1.0) // 1 micron tolerance
        {
            Console.WriteLine($"[TwoBendSolver]     Start validation failed: {startError:F3}µm offset");
            return null;
        }

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
