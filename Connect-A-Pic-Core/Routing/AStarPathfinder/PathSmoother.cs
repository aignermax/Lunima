using CAP_Core.Components;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Converts A* grid paths to smooth waveguide path segments.
/// Simplifies the path, then generates proper StraightSegment and BendSegment objects.
/// </summary>
public class PathSmoother
{
    private readonly PathfindingGrid _grid;
    private readonly double _minBendRadius;

    public PathSmoother(PathfindingGrid grid, double minBendRadius)
    {
        _grid = grid;
        _minBendRadius = minBendRadius;
    }

    /// <summary>
    /// Converts a grid path to waveguide segments.
    /// </summary>
    /// <param name="gridPath">Path from A* pathfinder</param>
    /// <param name="startPin">Start pin (for exact position)</param>
    /// <param name="endPin">End pin (for exact position)</param>
    /// <returns>Routed path with proper segments</returns>
    public RoutedPath ConvertToSegments(List<AStarNode> gridPath,
                                         PhysicalPin startPin, PhysicalPin endPin)
    {
        var routedPath = new RoutedPath();

        if (gridPath == null || gridPath.Count < 2)
        {
            return routedPath;
        }

        // Step 1: Simplify path - keep only corner points where direction changes
        var cornerPoints = ExtractCornerPoints(gridPath);

        // Step 2: Convert to physical coordinates
        var physicalPoints = cornerPoints
            .Select(n => _grid.GridToPhysical(n.X, n.Y))
            .ToList();

        // Step 3: Replace first and last with exact pin positions
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        if (physicalPoints.Count >= 2)
        {
            physicalPoints[0] = (startX, startY);
            physicalPoints[^1] = (endX, endY);
        }
        else
        {
            physicalPoints = new List<(double x, double y)> { (startX, startY), (endX, endY) };
        }

        // Step 4: Generate segments with bends at corners
        GenerateSegments(routedPath, physicalPoints);

        return routedPath;
    }

    /// <summary>
    /// Extracts only the corner points where direction changes.
    /// </summary>
    private List<AStarNode> ExtractCornerPoints(List<AStarNode> path)
    {
        if (path.Count <= 2)
            return new List<AStarNode>(path);

        var corners = new List<AStarNode> { path[0] };

        for (int i = 1; i < path.Count - 1; i++)
        {
            // Check if direction changes at this point
            if (path[i].Direction != path[i + 1].Direction)
            {
                corners.Add(path[i]);
            }
        }

        corners.Add(path[^1]);
        return corners;
    }

    /// <summary>
    /// Generates straight and bend segments connecting the corner points.
    /// </summary>
    private void GenerateSegments(RoutedPath path, List<(double x, double y)> points)
    {
        if (points.Count < 2)
            return;

        // Special case: only 2 points - single straight segment
        if (points.Count == 2)
        {
            var (x1, y1) = points[0];
            var (x2, y2) = points[1];
            double angle = Math.Atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
            path.Segments.Add(new StraightSegment(x1, y1, x2, y2, angle));
            return;
        }

        // Process each corner point
        // For each triplet of points (prev, current, next), we need to:
        // 1. Draw a straight segment from prev to (current - bend radius)
        // 2. Draw a bend from (current - bend radius) to (current + bend radius in new direction)
        // 3. Continue from there

        double currentX = points[0].x;
        double currentY = points[0].y;

        for (int i = 0; i < points.Count - 1; i++)
        {
            var (targetX, targetY) = points[i + 1];

            // Calculate angle of this segment
            double dx = targetX - currentX;
            double dy = targetY - currentY;
            double segmentLength = Math.Sqrt(dx * dx + dy * dy);

            if (segmentLength < 0.1)
            {
                // Skip degenerate segments
                continue;
            }

            double segmentAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

            // Check if we need a bend at the end of this segment
            bool needsBend = false;
            double nextAngle = segmentAngle;

            if (i < points.Count - 2)
            {
                var (nextX, nextY) = points[i + 2];
                double nextDx = nextX - targetX;
                double nextDy = nextY - targetY;
                double nextLength = Math.Sqrt(nextDx * nextDx + nextDy * nextDy);

                if (nextLength > 0.1)
                {
                    nextAngle = Math.Atan2(nextDy, nextDx) * 180 / Math.PI;
                    double angleDiff = Math.Abs(NormalizeAngle(nextAngle - segmentAngle));
                    needsBend = angleDiff > 5; // More than 5 degrees difference
                }
            }

            if (needsBend)
            {
                // We need a bend at the corner (targetX, targetY)
                // The bend needs _minBendRadius of space on each side

                // How much space do we need to leave for the bend?
                double bendSpace = _minBendRadius;

                // Shorten this segment to leave room for bend entry
                double shortenedLength = segmentLength - bendSpace;

                if (shortenedLength > 0.1)
                {
                    // Normalize direction
                    double dirX = dx / segmentLength;
                    double dirY = dy / segmentLength;

                    double shortenedX = currentX + dirX * shortenedLength;
                    double shortenedY = currentY + dirY * shortenedLength;

                    // Add straight segment up to bend start
                    path.Segments.Add(new StraightSegment(currentX, currentY,
                        shortenedX, shortenedY, segmentAngle));

                    // Add bend
                    AddBendSegment(path, shortenedX, shortenedY, segmentAngle, nextAngle);

                    // Update current position to bend end
                    var lastSegment = path.Segments.LastOrDefault();
                    if (lastSegment != null)
                    {
                        currentX = lastSegment.EndPoint.X;
                        currentY = lastSegment.EndPoint.Y;
                    }
                }
                else
                {
                    // Not enough room for proper bend - just add a sharp corner
                    path.Segments.Add(new StraightSegment(currentX, currentY,
                        targetX, targetY, segmentAngle));
                    currentX = targetX;
                    currentY = targetY;
                }
            }
            else
            {
                // No bend needed - go straight to target
                path.Segments.Add(new StraightSegment(currentX, currentY,
                    targetX, targetY, segmentAngle));
                currentX = targetX;
                currentY = targetY;
            }
        }

        // Ensure the path ends at the actual last point
        var finalPoint = points[^1];
        if (path.Segments.Count > 0)
        {
            var lastSeg = path.Segments[^1];
            double endDist = Math.Sqrt(
                Math.Pow(lastSeg.EndPoint.X - finalPoint.x, 2) +
                Math.Pow(lastSeg.EndPoint.Y - finalPoint.y, 2));

            if (endDist > 1.0)
            {
                // Add final segment to reach the actual end point
                double finalDx = finalPoint.x - lastSeg.EndPoint.X;
                double finalDy = finalPoint.y - lastSeg.EndPoint.Y;
                double finalAngle = Math.Atan2(finalDy, finalDx) * 180 / Math.PI;

                path.Segments.Add(new StraightSegment(
                    lastSeg.EndPoint.X, lastSeg.EndPoint.Y,
                    finalPoint.x, finalPoint.y, finalAngle));
            }
        }
    }

    /// <summary>
    /// Adds a 90-degree bend segment at a corner.
    /// </summary>
    private void AddBendSegment(RoutedPath path, double x, double y,
                                 double startAngle, double endAngle)
    {
        double sweepAngle = NormalizeAngle(endAngle - startAngle);

        // Clamp to 90-degree turns (A* only produces cardinal directions)
        if (Math.Abs(sweepAngle) > 90)
        {
            sweepAngle = Math.Sign(sweepAngle) * 90;
        }

        double bendDirection = Math.Sign(sweepAngle);
        if (bendDirection == 0) bendDirection = 1;

        // Calculate bend center (perpendicular to start direction)
        double startRad = startAngle * Math.PI / 180;
        double perpX = -Math.Sin(startRad) * bendDirection;
        double perpY = Math.Cos(startRad) * bendDirection;

        double centerX = x + perpX * _minBendRadius;
        double centerY = y + perpY * _minBendRadius;

        var bend = new BendSegment(centerX, centerY, _minBendRadius, startAngle, sweepAngle);
        path.Segments.Add(bend);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle <= -180) angle += 360;
        return angle;
    }
}
