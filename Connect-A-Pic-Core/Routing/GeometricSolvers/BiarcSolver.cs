using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.GeometricSolvers;

/// <summary>
/// Solves the biarc problem: finds two tangent circular arcs connecting two poses.
/// Pure geometric computation with no side effects.
/// </summary>
public class BiarcSolver
{
    /// <summary>
    /// Attempts to construct a biarc (two tangent circular arcs) connecting start to end.
    /// </summary>
    /// <param name="startX">Start X coordinate</param>
    /// <param name="startY">Start Y coordinate</param>
    /// <param name="startAngle">Start angle in degrees</param>
    /// <param name="endX">End X coordinate</param>
    /// <param name="endY">End Y coordinate</param>
    /// <param name="endEntryAngle">End entry angle in degrees (opposite of end pin angle)</param>
    /// <param name="firstBendDir">First bend direction: 1.0 (left) or -1.0 (right)</param>
    /// <param name="secondBendDir">Second bend direction: 1.0 (left) or -1.0 (right)</param>
    /// <param name="radius">Bend radius in micrometers</param>
    /// <returns>Tuple of (bend1, bend2) if successful, otherwise null</returns>
    public (BendSegment bend1, BendSegment bend2)? SolveBiarc(
        double startX, double startY, double startAngle,
        double endX, double endY, double endEntryAngle,
        double firstBendDir, double secondBendDir,
        double radius)
    {
        // Convert angles to radians
        double startRad = startAngle * Math.PI / 180;
        double endRad = endEntryAngle * Math.PI / 180;

        // Compute circle centers for first and second bends
        // Circle center is perpendicular to the direction, at distance = radius
        double c1X = startX + firstBendDir * radius * Math.Cos(startRad + Math.PI / 2);
        double c1Y = startY + firstBendDir * radius * Math.Sin(startRad + Math.PI / 2);

        double c2X = endX + secondBendDir * radius * Math.Cos(endRad + Math.PI / 2);
        double c2Y = endY + secondBendDir * radius * Math.Sin(endRad + Math.PI / 2);

        // Distance between centers
        double dx = c2X - c1X;
        double dy = c2Y - c1Y;
        double centerDistance = Math.Sqrt(dx * dx + dy * dy);

        // Circle-circle intersection requires distance <= 2*radius
        if (centerDistance > 2 * radius + 1e-6) // 1e-6 tolerance
            return null;

        // If circles are too close, they don't have a valid tangent point
        if (centerDistance < 0.01)
            return null;

        // Compute circle-circle intersection points
        // Using standard formula: https://mathworld.wolfram.com/Circle-CircleIntersection.html
        double a = centerDistance / 2.0;
        double hSquared = radius * radius - a * a;

        if (hSquared < 0)
            return null; // No intersection

        double h = Math.Sqrt(hSquared);

        // Midpoint between centers
        double midX = c1X + dx * 0.5;
        double midY = c1Y + dy * 0.5;

        // Perpendicular vector to line between centers (normalized)
        double perpX = -dy / centerDistance;
        double perpY = dx / centerDistance;

        // Two possible intersection points
        double junction1X = midX + perpX * h;
        double junction1Y = midY + perpY * h;
        double junction2X = midX - perpX * h;
        double junction2Y = midY - perpY * h;

        // Try both junction points - one should produce valid arcs
        var result1 = TryBuildArcsWithJunction(
            startX, startY, startAngle, c1X, c1Y, firstBendDir,
            endX, endY, endEntryAngle, c2X, c2Y, secondBendDir,
            junction1X, junction1Y, radius);

        if (result1 != null)
            return result1;

        var result2 = TryBuildArcsWithJunction(
            startX, startY, startAngle, c1X, c1Y, firstBendDir,
            endX, endY, endEntryAngle, c2X, c2Y, secondBendDir,
            junction2X, junction2Y, radius);

        return result2;
    }

    /// <summary>
    /// Attempts to build two arc segments using a specific junction point.
    /// </summary>
    private (BendSegment, BendSegment)? TryBuildArcsWithJunction(
        double startX, double startY, double startAngle,
        double c1X, double c1Y, double firstBendDir,
        double endX, double endY, double endEntryAngle,
        double c2X, double c2Y, double secondBendDir,
        double junctionX, double junctionY, double radius)
    {
        // Build first arc: from start to junction
        var bend1 = BuildArc(startX, startY, junctionX, junctionY, c1X, c1Y, firstBendDir, radius);
        if (bend1 == null) return null;

        // Build second arc: from junction to end
        var bend2 = BuildArc(junctionX, junctionY, endX, endY, c2X, c2Y, secondBendDir, radius);
        if (bend2 == null) return null;

        return (bend1, bend2);
    }

    /// <summary>
    /// Builds a single arc segment from start point to end point around a center.
    /// </summary>
    private BendSegment? BuildArc(
        double startX, double startY,
        double endX, double endY,
        double centerX, double centerY,
        double bendDir, double radius)
    {
        // Calculate angles from center to start and end points
        double angleToStart = Math.Atan2(startY - centerY, startX - centerX) * 180 / Math.PI;
        double angleToEnd = Math.Atan2(endY - centerY, endX - centerX) * 180 / Math.PI;

        // Calculate sweep angle (accounting for direction)
        double sweep = angleToEnd - angleToStart;

        // Normalize sweep to match bend direction
        if (bendDir > 0) // Left turn (counter-clockwise)
        {
            while (sweep < 0) sweep += 360;
            while (sweep > 360) sweep -= 360;
        }
        else // Right turn (clockwise)
        {
            while (sweep > 0) sweep -= 360;
            while (sweep < -360) sweep += 360;
        }

        // Sanity check: arc should be reasonable (not wrapping around)
        if (Math.Abs(sweep) > 350)
            return null;

        return new BendSegment(
            centerX, centerY,
            radius,
            angleToStart,
            sweep);
    }
}
