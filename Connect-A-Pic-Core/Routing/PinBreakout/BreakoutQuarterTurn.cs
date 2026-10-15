namespace CAP_Core.Routing.PinBreakout;

/// <summary>
/// Turned breakout geometry: a straight run out of the pin followed by one tangent 90° arc
/// towards a lateral side. Wires of a cluster that all turn the same way get straight runs
/// staggered by the routable pitch, so their arcs are translated copies of one another and
/// they leave the cluster in parallel lanes — the nested final bends the grid cannot place.
/// </summary>
public static class BreakoutQuarterTurn
{
    private const double QuarterTurnDegrees = 90.0;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Straight runs shorter than this (µm) are dropped as numerical noise.</summary>
    private const double MinStraightMicrometers = 1e-6;

    /// <summary>
    /// Builds the turned breakout from a pin outward.
    /// </summary>
    /// <param name="x">Pin X (µm).</param>
    /// <param name="y">Pin Y (µm).</param>
    /// <param name="headingDegrees">Outward pin heading (degrees).</param>
    /// <param name="straightRun">Straight run (µm) before the arc.</param>
    /// <param name="side">+1 turns towards heading + 90°, −1 towards heading − 90°.</param>
    /// <param name="radius">Arc radius (µm).</param>
    public static List<PathSegment> Build(
        double x, double y, double headingDegrees, double straightRun, int side, double radius)
    {
        var segments = new List<PathSegment>();
        double rad = headingDegrees * DegreesToRadians;
        double arcX = x, arcY = y;
        if (straightRun > MinStraightMicrometers)
        {
            arcX = x + straightRun * Math.Cos(rad);
            arcY = y + straightRun * Math.Sin(rad);
            segments.Add(new StraightSegment(x, y, arcX, arcY, headingDegrees));
        }
        double centerX = arcX - Math.Sin(rad) * radius * side;
        double centerY = arcY + Math.Cos(rad) * radius * side;
        segments.Add(new BendSegment(centerX, centerY, radius, headingDegrees, side * QuarterTurnDegrees));
        return segments;
    }
}
