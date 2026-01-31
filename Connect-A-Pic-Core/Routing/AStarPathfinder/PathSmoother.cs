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
    private readonly List<double> _allowedRadii;

    public PathSmoother(PathfindingGrid grid, double minBendRadius, List<double>? allowedRadii = null)
    {
        _grid = grid;
        _minBendRadius = minBendRadius;
        // Sort allowed radii ascending for easy selection
        _allowedRadii = allowedRadii?.OrderBy(r => r).ToList() ?? new List<double>();
    }

    /// <summary>
    /// Selects the appropriate bend radius from allowed values.
    /// Returns the smallest allowed radius that is >= requiredRadius.
    /// If no allowed radii are configured, returns the required radius.
    /// </summary>
    private double SelectBendRadius(double requiredRadius)
    {
        if (_allowedRadii.Count == 0)
        {
            // No constraints - use the required radius (but at least minBendRadius)
            return Math.Max(requiredRadius, _minBendRadius);
        }

        // Find the smallest allowed radius that fits
        foreach (var radius in _allowedRadii)
        {
            if (radius >= requiredRadius)
            {
                return radius;
            }
        }

        // If required radius is larger than all allowed, use the largest allowed
        // (this may cause issues, but it's better than nothing)
        return _allowedRadii[^1];
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
        // Pass pin angles for mathematically correct start and end segments
        double startAngle = startPin.GetAbsoluteAngle();
        double endInputAngle = NormalizeAngle(endPin.GetAbsoluteAngle() + 180);
        GenerateSegments(routedPath, physicalPoints, startAngle, endInputAngle);

        return routedPath;
    }

    /// <summary>
    /// Ensures there's enough straight distance at start and end for proper bends,
    /// AND that the first/last segments go in the correct pin direction.
    /// </summary>
    private void EnsureMinimumStraightRuns(List<(double x, double y)> points,
                                            PhysicalPin startPin, PhysicalPin endPin)
    {
        if (points.Count < 2) return;

        double minStraightRun = _minBendRadius * 2; // Need 2x bend radius for proper geometry

        // Get pin angles
        double startAngle = startPin.GetAbsoluteAngle();
        double endInputAngle = NormalizeAngle(endPin.GetAbsoluteAngle() + 180); // Direction waveguide approaches end pin

        var (startX, startY) = points[0];
        var (endX, endY) = points[^1];

        // ALWAYS ensure the first segment goes in the start pin's direction
        // Insert a waypoint along the pin direction if needed
        if (points.Count >= 2)
        {
            var (p1x, p1y) = points[1];
            double dx = p1x - startX;
            double dy = p1y - startY;
            double actualAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
            double angleDiff = Math.Abs(NormalizeAngle(actualAngle - startAngle));

            // If second point is not in the pin's direction, insert a waypoint
            if (angleDiff > 30) // More than 30 degrees off
            {
                double rad = startAngle * Math.PI / 180;
                double waypointX = startX + Math.Cos(rad) * minStraightRun;
                double waypointY = startY + Math.Sin(rad) * minStraightRun;
                points.Insert(1, (waypointX, waypointY));
            }
            else
            {
                // Check if the distance is sufficient
                double dist01 = Math.Sqrt(dx * dx + dy * dy);
                if (dist01 < minStraightRun && dist01 > 0.1)
                {
                    // Extend along the start pin direction
                    double rad = startAngle * Math.PI / 180;
                    double newX = startX + Math.Cos(rad) * minStraightRun;
                    double newY = startY + Math.Sin(rad) * minStraightRun;
                    points[1] = (newX, newY);
                }
            }
        }

        // ALWAYS ensure the last segment approaches from the correct direction
        // Insert a waypoint if needed
        if (points.Count >= 2)
        {
            var (pn1x, pn1y) = points[^2];
            double dx = endX - pn1x;
            double dy = endY - pn1y;
            double actualAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
            double angleDiff = Math.Abs(NormalizeAngle(actualAngle - endInputAngle));

            // If approach angle is wrong, insert a waypoint
            if (angleDiff > 30) // More than 30 degrees off
            {
                double rad = endInputAngle * Math.PI / 180;
                double waypointX = endX - Math.Cos(rad) * minStraightRun;
                double waypointY = endY - Math.Sin(rad) * minStraightRun;
                points.Insert(points.Count - 1, (waypointX, waypointY));
            }
            else
            {
                // Check if the distance is sufficient
                double distN = Math.Sqrt(dx * dx + dy * dy);
                if (distN < minStraightRun && distN > 0.1)
                {
                    // Extend backward along the end pin's input direction
                    double rad = endInputAngle * Math.PI / 180;
                    double newX = endX - Math.Cos(rad) * minStraightRun;
                    double newY = endY - Math.Sin(rad) * minStraightRun;
                    points[^2] = (newX, newY);
                }
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
    /// <param name="path">The path to add segments to</param>
    /// <param name="points">Corner points to connect</param>
    /// <param name="startOutputAngle">Required output angle from start pin</param>
    /// <param name="endInputAngle">Required approach angle to end pin</param>
    private void GenerateSegments(RoutedPath path, List<(double x, double y)> points,
                                   double startOutputAngle, double endInputAngle)
    {
        if (points.Count < 2)
            return;

        // Special case: only 2 points - single straight segment
        if (points.Count == 2)
        {
            var (x1, y1) = points[0];
            var (x2, y2) = points[1];
            // Use the start pin's output angle for the segment
            path.Segments.Add(new StraightSegment(x1, y1, x2, y2, startOutputAngle));
            return;
        }

        // Process each corner point
        // For each triplet of points (prev, current, next), we need to:
        // 1. Draw a straight segment from prev to (current - bend radius)
        // 2. Draw a bend from (current - bend radius) to (current + bend radius in new direction)
        // 3. Continue from there

        double currentX = points[0].x;
        double currentY = points[0].y;
        double currentAngle = startOutputAngle; // Start with the pin's output angle

        for (int i = 0; i < points.Count - 1; i++)
        {
            var (targetX, targetY) = points[i + 1];

            // Calculate angle of this segment based on point positions
            double dx = targetX - currentX;
            double dy = targetY - currentY;
            double segmentLength = Math.Sqrt(dx * dx + dy * dy);

            if (segmentLength < 0.1)
            {
                // Skip degenerate segments
                continue;
            }

            double targetAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

            // For the first segment, check if we need to add a bend to transition
            // from the start pin's direction to the path direction
            if (i == 0)
            {
                double startAngleDiff = Math.Abs(NormalizeAngle(targetAngle - startOutputAngle));
                if (startAngleDiff > 10) // Need to turn from pin direction
                {
                    // Add a bend at the start to transition from pin direction to path direction
                    double bendRadius = SelectBendRadiusForSpace(_minBendRadius);
                    double availableForBend = segmentLength / 2;

                    if (availableForBend >= bendRadius * 0.5)
                    {
                        // First go straight in pin direction, then bend
                        double straightLength = Math.Min(bendRadius, segmentLength / 3);
                        double rad = startOutputAngle * Math.PI / 180;
                        double straightEndX = currentX + Math.Cos(rad) * straightLength;
                        double straightEndY = currentY + Math.Sin(rad) * straightLength;

                        path.Segments.Add(new StraightSegment(currentX, currentY,
                            straightEndX, straightEndY, startOutputAngle));

                        // Add bend to transition to target direction
                        AddBendSegmentWithRadius(path, straightEndX, straightEndY,
                            startOutputAngle, targetAngle, bendRadius);

                        var lastSeg = path.Segments[^1];
                        currentX = lastSeg.EndPoint.X;
                        currentY = lastSeg.EndPoint.Y;
                        currentAngle = lastSeg.EndAngleDegrees;

                        // Recalculate remaining distance to target
                        dx = targetX - currentX;
                        dy = targetY - currentY;
                        segmentLength = Math.Sqrt(dx * dx + dy * dy);

                        if (segmentLength < 0.1)
                            continue;
                    }
                }
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

        // Ensure the path ends at the actual last point with correct approach angle
        var finalPoint = points[^1];
        if (path.Segments.Count > 0)
        {
            var lastSeg = path.Segments[^1];
            double endDist = Math.Sqrt(
                Math.Pow(lastSeg.EndPoint.X - finalPoint.x, 2) +
                Math.Pow(lastSeg.EndPoint.Y - finalPoint.y, 2));

            if (endDist > 0.5)
            {
                // Need to connect to the end point - do it mathematically correct
                // Calculate the current approach angle
                double finalApproachAngle = lastSeg.EndAngleDegrees;
                double angleDiff = Math.Abs(NormalizeAngle(endInputAngle - finalApproachAngle));

                if (angleDiff > 10) // Need to turn to approach correctly
                {
                    // Add a bend to align with the end approach direction
                    double bendRadius = SelectBendRadiusForSpace(_minBendRadius);

                    // Calculate where to start the bend
                    // We need enough space for the bend and final straight
                    double straightToEnd = endDist - bendRadius;

                    if (straightToEnd > 0.5)
                    {
                        // Add bend first, then straight to end
                        AddBendSegmentWithRadius(path, lastSeg.EndPoint.X, lastSeg.EndPoint.Y,
                            finalApproachAngle, endInputAngle, bendRadius);

                        var bendEnd = path.Segments[^1];
                        path.Segments.Add(new StraightSegment(
                            bendEnd.EndPoint.X, bendEnd.EndPoint.Y,
                            finalPoint.x, finalPoint.y, endInputAngle));
                    }
                    else
                    {
                        // Not enough space for proper bend - use tighter radius
                        double tightRadius = Math.Max(endDist * 0.4, 2.0);
                        AddBendSegmentWithRadius(path, lastSeg.EndPoint.X, lastSeg.EndPoint.Y,
                            finalApproachAngle, endInputAngle, tightRadius);

                        // Final connection if needed
                        var bendEnd = path.Segments[^1];
                        double remainingDist = Math.Sqrt(
                            Math.Pow(bendEnd.EndPoint.X - finalPoint.x, 2) +
                            Math.Pow(bendEnd.EndPoint.Y - finalPoint.y, 2));
                        if (remainingDist > 0.5)
                        {
                            path.Segments.Add(new StraightSegment(
                                bendEnd.EndPoint.X, bendEnd.EndPoint.Y,
                                finalPoint.x, finalPoint.y, endInputAngle));
                        }
                    }
                }
                else
                {
                    // Already aligned - just add straight segment
                    path.Segments.Add(new StraightSegment(
                        lastSeg.EndPoint.X, lastSeg.EndPoint.Y,
                        finalPoint.x, finalPoint.y, finalApproachAngle));
                }
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
    /// Adds a bend segment at a corner with a specified available space.
    /// The actual radius used will be selected from allowed radii (foundry-style).
    /// </summary>
    private void AddBendSegmentWithRadius(RoutedPath path, double x, double y,
                                           double startAngle, double endAngle, double availableSpace)
    {
        double sweepAngle = NormalizeAngle(endAngle - startAngle);

        // Clamp to 90-degree turns (A* only produces cardinal directions)
        if (Math.Abs(sweepAngle) > 90)
        {
            sweepAngle = Math.Sign(sweepAngle) * 90;
        }

        // Select the appropriate bend radius from allowed values
        // We need a radius that fits in the available space
        double radius = SelectBendRadiusForSpace(availableSpace);

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

    /// <summary>
    /// Selects the largest allowed radius that fits in the available space.
    /// </summary>
    private double SelectBendRadiusForSpace(double availableSpace)
    {
        if (_allowedRadii.Count == 0)
        {
            // No constraints - use available space but at least minBendRadius
            return Math.Max(availableSpace, _minBendRadius);
        }

        // Find the largest allowed radius that fits
        double selected = _allowedRadii[0]; // Start with smallest
        foreach (var radius in _allowedRadii)
        {
            if (radius <= availableSpace)
            {
                selected = radius;
            }
            else
            {
                break; // Radii are sorted, so we can stop here
            }
        }

        return selected;
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle <= -180) angle += 360;
        return angle;
    }
}
