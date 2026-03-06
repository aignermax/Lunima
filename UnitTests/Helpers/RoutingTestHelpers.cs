using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Helpers;

/// <summary>
/// Helper methods for routing tests, including physical validity assertions.
/// </summary>
public static class RoutingTestHelpers
{
    /// <summary>
    /// Asserts that a routed path is physically valid for photonic waveguides.
    /// Checks G1 continuity, bend geometry, and path length sanity.
    /// </summary>
    /// <param name="path">The routed path to validate</param>
    /// <param name="minRadius">Minimum allowed bend radius</param>
    /// <param name="manhattanDistance">Manhattan distance between endpoints (for loop detection)</param>
    public static void AssertPhysicallyValid(RoutedPath path, double minRadius, double? manhattanDistance = null)
    {
        path.ShouldNotBeNull("Path should not be null");
        path.Segments.ShouldNotBeEmpty("Path should have segments");

        // Check G1 tangent continuity
        AssertG1TangentContinuity(path);

        // Check bend circle validity
        AssertBendCircleValidity(path, minRadius);

        // Check tangent perpendicular to radius at bend start
        AssertBendTangentPerpendicular(path);

        // Check path length sanity (loop detection)
        if (manhattanDistance.HasValue)
        {
            AssertPathLengthSanity(path, manhattanDistance.Value);
        }
    }

    /// <summary>
    /// Asserts G1 tangent continuity: no sharp corners between segments.
    /// </summary>
    private static void AssertG1TangentContinuity(RoutedPath path)
    {
        for (int i = 0; i < path.Segments.Count - 1; i++)
        {
            double exitAngle = path.Segments[i].EndAngleDegrees;
            double entryAngle = path.Segments[i + 1].StartAngleDegrees;

            double angleDiff = Math.Abs(NormalizeAngle(exitAngle - entryAngle));

            angleDiff.ShouldBeLessThan(0.5,
                $"G1 continuity violated between segments {i} and {i + 1}: " +
                $"exit angle={exitAngle:F1}° vs entry angle={entryAngle:F1}° (diff={angleDiff:F1}°)");
        }
    }

    /// <summary>
    /// Asserts that bend segments have valid circle geometry.
    /// The center must be equidistant from start and end points.
    /// </summary>
    private static void AssertBendCircleValidity(RoutedPath path, double minRadius)
    {
        for (int i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i] is not BendSegment bend)
                continue;

            // Check minimum radius
            bend.RadiusMicrometers.ShouldBeGreaterThanOrEqualTo(minRadius * 0.99,
                $"Bend {i} radius {bend.RadiusMicrometers:F2}µm is below minimum {minRadius}µm");

            // Check that center is equidistant from start and end
            double distStart = Distance(bend.Center, bend.StartPoint);
            double distEnd = Distance(bend.Center, bend.EndPoint);

            Math.Abs(distStart - bend.RadiusMicrometers).ShouldBeLessThan(0.01,
                $"Bend {i} center-to-start distance {distStart:F2}µm != radius {bend.RadiusMicrometers:F2}µm");

            Math.Abs(distEnd - bend.RadiusMicrometers).ShouldBeLessThan(0.01,
                $"Bend {i} center-to-end distance {distEnd:F2}µm != radius {bend.RadiusMicrometers:F2}µm");
        }
    }

    /// <summary>
    /// Asserts that at the start of a bend, the tangent is perpendicular to the radius vector.
    /// </summary>
    private static void AssertBendTangentPerpendicular(RoutedPath path)
    {
        for (int i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i] is not BendSegment bend)
                continue;

            // Calculate radius angle (from center to start point)
            double radiusAngle = Math.Atan2(
                bend.StartPoint.Y - bend.Center.Y,
                bend.StartPoint.X - bend.Center.X) * 180 / Math.PI;

            // Tangent should be perpendicular to radius (±90°)
            double angleDiff = NormalizeAngle(bend.StartAngleDegrees - radiusAngle);

            double perpDiff = Math.Abs(Math.Abs(angleDiff) - 90);

            perpDiff.ShouldBeLessThan(0.5,
                $"Bend {i} tangent not perpendicular to radius: " +
                $"tangent={bend.StartAngleDegrees:F1}° radius={radiusAngle:F1}° (diff from 90°={perpDiff:F1}°)");
        }
    }

    /// <summary>
    /// Asserts that the path length is reasonable (< 3× Manhattan distance).
    /// Detects loops and excessive detours.
    /// </summary>
    private static void AssertPathLengthSanity(RoutedPath path, double manhattanDistance)
    {
        double ratio = path.TotalLengthMicrometers / Math.Max(manhattanDistance, 1.0);

        ratio.ShouldBeLessThan(3.0,
            $"Path is {ratio:F1}× Manhattan distance ({path.TotalLengthMicrometers:F1}µm vs " +
            $"{manhattanDistance:F1}µm). This indicates a loop or unnecessary detour.");
    }

    /// <summary>
    /// Normalizes an angle to the range [-180, 180).
    /// </summary>
    private static double NormalizeAngle(double angle)
    {
        while (angle >= 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    /// <summary>
    /// Calculates the Euclidean distance between two points.
    /// </summary>
    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
