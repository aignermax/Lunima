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

        // Step 4: Ensure minimum straight run at start and end (for pin alignment)
        // Waveguides must exit/enter pins perpendicular - ensure enough space for bends
        EnsureMinimumStraightRuns(physicalPoints, startPin, endPin);

        // Step 5: Ensure minimum spacing between corner points for proper bends
        // Each bend needs space on both sides, so consecutive corners need 2x bend radius
        EnsureMinimumCornerSpacing(physicalPoints);

        // Step 6: Generate segments with bends at corners
        GenerateSegments(routedPath, physicalPoints);

        return routedPath;
    }

    /// <summary>
    /// Ensures there's enough straight distance at start and end for proper bends.
    /// </summary>
    private void EnsureMinimumStraightRuns(List<(double x, double y)> points,
                                            PhysicalPin startPin, PhysicalPin endPin)
    {
        if (points.Count < 2) return;

        double minStraightRun = _minBendRadius * 2; // Need 2x bend radius for proper geometry

        // Check start: if first segment is too short, extend the second point outward
        if (points.Count >= 3)
        {
            var (p0x, p0y) = points[0];
            var (p1x, p1y) = points[1];
            double dist01 = Math.Sqrt(Math.Pow(p1x - p0x, 2) + Math.Pow(p1y - p0y, 2));

            if (dist01 < minStraightRun && dist01 > 0.1)
            {
                // Extend along the start pin direction
                double startAngle = startPin.GetAbsoluteAngle();
                double rad = startAngle * Math.PI / 180;
                double newX = p0x + Math.Cos(rad) * minStraightRun;
                double newY = p0y + Math.Sin(rad) * minStraightRun;
                points[1] = (newX, newY);
            }
        }

        // Check end: if last segment is too short, adjust second-to-last point
        if (points.Count >= 3)
        {
            var (pnx, pny) = points[^1];
            var (pn1x, pn1y) = points[^2];
            double distN = Math.Sqrt(Math.Pow(pnx - pn1x, 2) + Math.Pow(pny - pn1y, 2));

            if (distN < minStraightRun && distN > 0.1)
            {
                // Extend backward along the end pin's input direction
                double endAngle = endPin.GetAbsoluteAngle() + 180; // Input direction (opposite of pin direction)
                double rad = NormalizeAngle(endAngle) * Math.PI / 180;
                double newX = pnx + Math.Cos(rad) * minStraightRun;
                double newY = pny + Math.Sin(rad) * minStraightRun;
                points[^2] = (newX, newY);
            }
        }
    }

    /// <summary>
    /// Ensures minimum spacing between consecutive corner points.
    /// When two corners are too close, they can't both have proper bends.
    /// This method adjusts or removes intermediate points to ensure proper spacing.
    /// </summary>
    private void EnsureMinimumCornerSpacing(List<(double x, double y)> points)
    {
        if (points.Count < 3) return;

        // Minimum distance between corners: each bend needs radius on both sides
        // So two consecutive bends need at least 2x bend radius between their corner points
        double minCornerSpacing = _minBendRadius * 2;

        // We may need multiple passes to handle cascading issues
        bool changed = true;
        int maxIterations = 10;
        int iteration = 0;

        while (changed && iteration < maxIterations)
        {
            changed = false;
            iteration++;

            // Check spacing between consecutive interior points (not start/end)
            for (int i = 1; i < points.Count - 2; i++)
            {
                var (x1, y1) = points[i];
                var (x2, y2) = points[i + 1];
                double dist = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

                if (dist < minCornerSpacing && dist > 0.1)
                {
                    // Points are too close - merge them by removing the second one
                    // and adjusting the segment to go through the midpoint direction
                    // For simplicity, we'll just remove the second point
                    // The path will still work, just with one less corner
                    points.RemoveAt(i + 1);
                    changed = true;
                    break; // Restart the loop after modification
                }
            }
        }
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
                // Calculate available space for the bend
                // Use at least half the segment length for the bend, but not more than _minBendRadius
                double availableBendSpace = Math.Min(_minBendRadius, segmentLength / 2);
                double shortenedLength = segmentLength - availableBendSpace;

                if (shortenedLength > 0.1 && availableBendSpace > 1.0)
                {
                    // Normalize direction
                    double dirX = dx / segmentLength;
                    double dirY = dy / segmentLength;

                    double shortenedX = currentX + dirX * shortenedLength;
                    double shortenedY = currentY + dirY * shortenedLength;

                    // Add straight segment up to bend start
                    path.Segments.Add(new StraightSegment(currentX, currentY,
                        shortenedX, shortenedY, segmentAngle));

                    // Add bend with available radius (may be smaller than _minBendRadius if tight)
                    AddBendSegmentWithRadius(path, shortenedX, shortenedY, segmentAngle, nextAngle, availableBendSpace);

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
                    // Very short segment - create a tight bend at the corner point
                    // Still better than a sharp corner for waveguide routing
                    double tightRadius = Math.Max(segmentLength * 0.4, 2.0); // At least 2µm radius
                    AddBendSegmentWithRadius(path, currentX, currentY, segmentAngle, nextAngle, tightRadius);

                    var lastSegment = path.Segments.LastOrDefault();
                    if (lastSegment != null)
                    {
                        currentX = lastSegment.EndPoint.X;
                        currentY = lastSegment.EndPoint.Y;
                    }
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
    /// Adds a 90-degree bend segment at a corner using the default minimum bend radius.
    /// </summary>
    private void AddBendSegment(RoutedPath path, double x, double y,
                                 double startAngle, double endAngle)
    {
        AddBendSegmentWithRadius(path, x, y, startAngle, endAngle, _minBendRadius);
    }

    /// <summary>
    /// Adds a bend segment at a corner with a specified radius.
    /// This allows for tighter bends when space is limited.
    /// </summary>
    private void AddBendSegmentWithRadius(RoutedPath path, double x, double y,
                                           double startAngle, double endAngle, double radius)
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

        double centerX = x + perpX * radius;
        double centerY = y + perpY * radius;

        var bend = new BendSegment(centerX, centerY, radius, startAngle, sweepAngle);
        path.Segments.Add(bend);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle <= -180) angle += 360;
        return angle;
    }
}
