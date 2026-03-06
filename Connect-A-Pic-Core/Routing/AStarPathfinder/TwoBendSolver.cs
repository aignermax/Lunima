using CAP_Core.Components;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Attempts to connect two pins using exactly two circular bends and no straight segments.
/// Handles simple near-aligned cases before falling back to A* pathfinding.
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
            (1.0, 1.0),   // Left-Left (positive, positive)
            (1.0, -1.0),  // Left-Right (positive, negative)
            (-1.0, 1.0),  // Right-Left (negative, positive)
            (-1.0, -1.0)  // Right-Right (negative, negative)
        };

        foreach (var (firstDir, secondDir) in bendCombinations)
        {
            var path = TryBuildTwoBendPath(
                startX, startY, startAngle,
                endX, endY, endEntryAngle,
                firstDir, secondDir);

            if (path != null)
                return path;
        }

        return null;
    }

    /// <summary>
    /// Attempts to construct a two-bend path with specified bend directions.
    /// </summary>
    private RoutedPath? TryBuildTwoBendPath(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle,
        double firstBendDir, double secondBendDir)
    {
        // We need to solve for two bends that connect (startX, startY, startAngle) to (endX, endY, endEntryAngle)
        //
        // Geometry:
        // - First bend: center at C1, radius R1, starting at angle startAngle
        // - Second bend: center at C2, radius R2, ending at angle endEntryAngle
        // - The two bends must be tangent (first bend's end point = second bend's start point)
        // - The two bends must have matching angles at the junction point

        // For simplicity, use minimum bend radius for both bends
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

        // For two bends to connect tangentially with equal radii,
        // the centers must be exactly 2*radius apart (for same direction bends)
        // or some other specific distance (for opposite direction bends)

        // Calculate the angle from C1 to C2
        double centerAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        // Calculate the sweep angle for the first bend
        // The first bend must end pointing toward the junction point
        // For a bend with center C1 and direction firstBendDir,
        // the tangent angle at any point is perpendicular to the radius

        // The junction point lies on the circle centered at C1 with radius R1
        // We need to find where this circle intersects with the circle centered at C2 with radius R2

        // For equal radii, if the bends are in the same direction (both left or both right),
        // the junction point is at the midpoint between centers, but this only works if centers are 2R apart

        // For opposite directions, the geometry is more complex

        // Simplified approach: Try to construct the bends and validate the result

        if (firstBendDir == secondBendDir)
        {
            // Same direction bends - centers should be 2*radius apart for tangency
            if (Math.Abs(centerDistance - 2.0 * radius) > radius * 0.5)
                return null; // Geometry doesn't fit

            // Junction point is midway between centers
            double junctionX = (c1X + c2X) / 2.0;
            double junctionY = (c1Y + c2Y) / 2.0;

            // Calculate angle at junction
            double junctionAngle = Math.Atan2(junctionY - c1Y, junctionX - c1X) * 180.0 / Math.PI;
            junctionAngle += (firstBendDir > 0 ? 90 : -90);

            // First bend: from startAngle to junctionAngle
            double sweep1 = AngleUtilities.NormalizeAngle(junctionAngle - startAngle);

            // Second bend: from junctionAngle to endEntryAngle
            double sweep2 = AngleUtilities.NormalizeAngle(endEntryAngle - junctionAngle);

            // Validate sweep angles have correct sign
            if (Math.Sign(sweep1) != Math.Sign(firstBendDir) || Math.Sign(sweep2) != Math.Sign(secondBendDir))
                return null;

            // Create bend segments
            var bend1 = new BendSegment(c1X, c1Y, radius, startAngle, sweep1);
            var bend2 = new BendSegment(c2X, c2Y, radius, junctionAngle, sweep2);

            // Validate end position and angle
            if (!ValidateEndpoint(bend2, endX, endY, endEntryAngle))
                return null;

            // Check for obstacles
            if (IsSegmentBlocked(bend1) || IsSegmentBlocked(bend2))
                return null;

            // Create and return path
            var path = new RoutedPath();
            path.Segments.Add(bend1);
            path.Segments.Add(bend2);
            return path;
        }
        else
        {
            // Opposite direction bends - more complex geometry
            // This requires solving for intersection of two circles with opposite curvatures
            // For now, return null (can be implemented later if needed)
            return null;
        }
    }

    /// <summary>
    /// Validates that a bend segment ends at the expected position and angle.
    /// </summary>
    private bool ValidateEndpoint(BendSegment bend, double expectedX, double expectedY, double expectedAngle)
    {
        const double positionTolerance = 0.1; // micrometers
        const double angleTolerance = 2.0;    // degrees

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
