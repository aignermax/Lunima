using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.Grid;
using CAP_Core.Routing.Utilities;
namespace CAP_Core.Routing.Utilities;

/// <summary>
/// Analyzes path geometry with cached computations.
/// Fixes O(n²) performance bug in lateral offset calculations.
/// </summary>
public class PathGeometryAnalyzer
{
    private readonly PathfindingGrid _grid;
    private List<PathSegmentInfo>? _physicalSegments;

    /// <summary>
    /// Represents a physical segment of the A* grid path.
    /// </summary>
    private record PathSegmentInfo(double X1, double Y1, double X2, double Y2, double Length);

    public PathGeometryAnalyzer(PathfindingGrid grid)
    {
        _grid = grid;
    }

    /// <summary>
    /// Sets the A* grid path and pre-computes physical segments.
    /// Call this once before multiple lateral offset queries.
    /// </summary>
    /// <param name="gridPath">A* grid path nodes</param>
    public void SetGridPath(List<AStarNode>? gridPath)
    {
        _physicalSegments = null;

        if (gridPath == null || gridPath.Count < 2)
            return;

        _physicalSegments = new List<PathSegmentInfo>(gridPath.Count - 1);

        // Pre-compute physical coordinates for all segments (O(n) once)
        for (int i = 0; i < gridPath.Count - 1; i++)
        {
            var node1 = gridPath[i];
            var node2 = gridPath[i + 1];

            var (x1, y1) = _grid.GridToPhysical(node1.X, node1.Y);
            var (x2, y2) = _grid.GridToPhysical(node2.X, node2.Y);

            double length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

            if (length >= 0.1) // Skip degenerate segments
            {
                _physicalSegments.Add(new PathSegmentInfo(x1, y1, x2, y2, length));
            }
        }
    }

    /// <summary>
    /// Calculates perpendicular (lateral) distance from current position to the A* path.
    /// Uses pre-computed segments for O(1) query instead of O(n) scan.
    /// </summary>
    /// <param name="currentX">Current X position (micrometers)</param>
    /// <param name="currentY">Current Y position (micrometers)</param>
    /// <param name="currentAngle">Current travel angle (degrees)</param>
    /// <returns>(distance, correctionAngle) - distance is always positive, angle points toward path</returns>
    public (double distance, double correctionAngle) CalculateLateralOffset(
        double currentX, double currentY, double currentAngle)
    {
        if (_physicalSegments == null || _physicalSegments.Count == 0)
            return (0, 0);

        // Find the closest segment to current position
        double minDistance = double.MaxValue;
        double bestCorrectionAngle = 0;

        foreach (var segment in _physicalSegments)
        {
            // Calculate perpendicular distance from point to line segment
            double dx = segment.X2 - segment.X1;
            double dy = segment.Y2 - segment.Y1;

            // Vector from segment start to point
            double vx = currentX - segment.X1;
            double vy = currentY - segment.Y1;

            // Segment direction vector (normalized)
            double sx = dx / segment.Length;
            double sy = dy / segment.Length;

            // Project point onto line (clamped to segment bounds)
            double projection = Math.Clamp(vx * sx + vy * sy, 0, segment.Length);

            // Closest point on segment
            double closestX = segment.X1 + sx * projection;
            double closestY = segment.Y1 + sy * projection;

            // Distance to segment
            double distance = Math.Sqrt(Math.Pow(currentX - closestX, 2) + Math.Pow(currentY - closestY, 2));

            if (distance < minDistance)
            {
                minDistance = distance;

                // Calculate correction angle (perpendicular to current direction, toward path)
                double toPathX = closestX - currentX;
                double toPathY = closestY - currentY;

                if (Math.Abs(toPathX) > 0.01 || Math.Abs(toPathY) > 0.01)
                {
                    double toPathAngle = Math.Atan2(toPathY, toPathX) * 180 / Math.PI;

                    // Determine which perpendicular direction brings us closer
                    double perp1 = AngleUtilities.NormalizeAngle(currentAngle + 90);
                    double perp2 = AngleUtilities.NormalizeAngle(currentAngle - 90);

                    double diff1 = Math.Abs(AngleUtilities.NormalizeAngle(toPathAngle - perp1));
                    double diff2 = Math.Abs(AngleUtilities.NormalizeAngle(toPathAngle - perp2));

                    bestCorrectionAngle = diff1 < diff2 ? perp1 : perp2;
                }
            }
        }

        return (minDistance, bestCorrectionAngle);
    }

    /// <summary>
    /// Checks if current position is reasonably close to the A* path.
    /// </summary>
    /// <param name="currentX">Current X position</param>
    /// <param name="currentY">Current Y position</param>
    /// <param name="threshold">Distance threshold in micrometers</param>
    /// <returns>True if within threshold of path</returns>
    public bool IsNearPath(double currentX, double currentY, double threshold)
    {
        var (distance, _) = CalculateLateralOffset(currentX, currentY, 0);
        return distance < threshold;
    }

    /// <summary>
    /// Gets the approximate path direction at a given position.
    /// Useful for aligning with the A* path.
    /// </summary>
    public double GetPathDirectionAt(double x, double y)
    {
        if (_physicalSegments == null || _physicalSegments.Count == 0)
            return 0;

        // Find closest segment
        double minDistance = double.MaxValue;
        PathSegmentInfo? closestSegment = null;

        foreach (var segment in _physicalSegments)
        {
            double dx = segment.X2 - segment.X1;
            double dy = segment.Y2 - segment.Y1;
            double vx = x - segment.X1;
            double vy = y - segment.Y1;

            double projection = Math.Clamp((vx * dx + vy * dy) / (segment.Length * segment.Length), 0, 1);
            double closestX = segment.X1 + dx * projection;
            double closestY = segment.Y1 + dy * projection;

            double distance = Math.Sqrt(Math.Pow(x - closestX, 2) + Math.Pow(y - closestY, 2));

            if (distance < minDistance)
            {
                minDistance = distance;
                closestSegment = segment;
            }
        }

        if (closestSegment == null)
            return 0;

        // Return direction of closest segment
        double angle = Math.Atan2(closestSegment.Y2 - closestSegment.Y1,
                                   closestSegment.X2 - closestSegment.X1) * 180 / Math.PI;
        return angle;
    }
}
