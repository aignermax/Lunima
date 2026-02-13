using CAP_Core.Components;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// Routing strategy for waveguide connections.
/// </summary>
public enum RoutingStrategy
{
    /// <summary>Try strategies in order: Straight, S-Bend, A*, Manhattan</summary>
    Auto,
    /// <summary>Direct line (only works if pins are aligned)</summary>
    Straight,
    /// <summary>Two opposing bends (works for parallel offset pins)</summary>
    SBend,
    /// <summary>Simple manhattan routing with bends (always works)</summary>
    Manhattan,
    /// <summary>A* pathfinding with obstacle avoidance</summary>
    AStar
}

/// <summary>
/// Routes waveguides between physical pins, generating path segments.
/// </summary>
public class WaveguideRouter
{
    /// <summary>
    /// Minimum bend radius in micrometers. Violating this causes high loss.
    /// </summary>
    public double MinBendRadiusMicrometers { get; set; } = 10.0;

    /// <summary>
    /// Allowed bend radii in micrometers (foundry-style discrete values).
    /// If empty, any radius >= MinBendRadiusMicrometers is allowed.
    /// When set, bends will snap to the smallest allowed radius that fits.
    /// Example: [5, 10, 20, 50] means only these four radii are used.
    /// </summary>
    public List<double> AllowedBendRadii { get; set; } = new() { 5, 10, 20, 50 };

    /// <summary>
    /// Minimum spacing between waveguides in micrometers.
    /// </summary>
    public double MinWaveguideSpacingMicrometers { get; set; } = 2.0;

    /// <summary>
    /// List of obstacles (component bounding boxes) to route around.
    /// </summary>
    public List<RoutingObstacle> Obstacles { get; } = new();

    /// <summary>
    /// Current routing strategy.
    /// </summary>
    public RoutingStrategy Strategy { get; set; } = RoutingStrategy.Auto;

    /// <summary>
    /// The pathfinding grid for A* routing. Must be initialized before using AStar strategy.
    /// </summary>
    public PathfindingGrid? PathfindingGrid { get; private set; }

    /// <summary>
    /// Cost calculator for A* routing.
    /// </summary>
    public RoutingCostCalculator CostCalculator { get; } = new();

    /// <summary>
    /// Grid cell size in micrometers for A* pathfinding.
    /// Larger values = faster routing but less precise paths.
    /// Should be smaller than MinBendRadiusMicrometers for smooth curves.
    /// With realistic component sizes (pins 5-10µm apart), use 2µm for precision.
    /// </summary>
    public double AStarCellSize { get; set; } = 2.0;

    /// <summary>
    /// Clearance padding around components in micrometers.
    /// Waveguides will maintain at least this distance from component edges.
    /// </summary>
    public double ObstaclePaddingMicrometers { get; set; } = 5.0;

    /// <summary>
    /// Initializes the pathfinding grid for A* routing.
    /// Call this before routing if using AStar strategy.
    /// </summary>
    /// <param name="minX">Minimum X bound in micrometers</param>
    /// <param name="minY">Minimum Y bound in micrometers</param>
    /// <param name="maxX">Maximum X bound in micrometers</param>
    /// <param name="maxY">Maximum Y bound in micrometers</param>
    /// <param name="components">Components to mark as obstacles</param>
    /// <param name="cellSize">Optional cell size override</param>
    public void InitializePathfindingGrid(double minX, double minY, double maxX, double maxY,
                                           IEnumerable<Component> components,
                                           double? cellSize = null)
    {
        double size = cellSize ?? AStarCellSize;
        PathfindingGrid = new PathfindingGrid(minX, minY, maxX, maxY, size, ObstaclePaddingMicrometers);
        PathfindingGrid.RebuildFromComponents(components);

        CostCalculator.CellSizeMicrometers = size;
        CostCalculator.MinBendRadiusMicrometers = MinBendRadiusMicrometers;
        CostCalculator.MinStraightRunCells = (int)Math.Ceiling(MinBendRadiusMicrometers * 2 / size);
    }

    /// <summary>
    /// Updates obstacle for a single component (after move).
    /// </summary>
    public void UpdateComponentObstacle(Component component)
    {
        PathfindingGrid?.UpdateComponentObstacle(component);
    }

    /// <summary>
    /// Removes obstacle for a deleted component.
    /// </summary>
    public void RemoveComponentObstacle(Component component)
    {
        PathfindingGrid?.RemoveComponentObstacle(component);
    }

    /// <summary>
    /// Adds obstacle for a new component.
    /// </summary>
    public void AddComponentObstacle(Component component)
    {
        PathfindingGrid?.AddComponentObstacle(component);
    }

    /// <summary>
    /// Routes a waveguide between two pins.
    /// Strategy order:
    /// 1. Straight line (if pins are aligned)
    /// 2. S-bend (smooth curves, flexible angles)
    /// 3. Manhattan (90° turns only, more predictable)
    /// 4. A* pathfinding (obstacle avoidance)
    /// </summary>
    public RoutedPath Route(PhysicalPin startPin, PhysicalPin endPin)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endAngle = endPin.GetAbsoluteAngle();

        // The end pin's "input" direction is opposite to its defined angle
        double endInputAngle = NormalizeAngle(endAngle + 180);

        // 1. Try straight line first (fastest, simplest)
        var path = new RoutedPath();
        if (TryRouteStraight(startX, startY, startAngle, endX, endY, endInputAngle, path))
        {
            if (!IsPathBlocked(path.Segments))
            {
                return path;
            }
        }

        // Calculate forward and lateral distances for routing decisions
        double startRad = startAngle * Math.PI / 180;
        double fwdX = Math.Cos(startRad);
        double fwdY = Math.Sin(startRad);
        double dx = endX - startX;
        double dy = endY - startY;
        double forwardDist = dx * fwdX + dy * fwdY;
        double leftX = -fwdY;
        double leftY = fwdX;
        double lateralDist = dx * leftX + dy * leftY;

        // 2. Try S-bend (smooth curves, flexible angles) - only if target is forward
        path = new RoutedPath();
        if (forwardDist >= MinBendRadiusMicrometers)
        {
            if (TryRouteSBend(startX, startY, startAngle, endX, endY, endInputAngle, path))
            {
                if (path.IsValid && !IsPathBlocked(path.Segments))
                {
                    return path;
                }
            }
        }

        // 3. Try U-turn if target is behind us
        if (forwardDist < MinBendRadiusMicrometers)
        {
            path = new RoutedPath();
            if (TryRouteUturn(startX, startY, startAngle, endX, endY, endInputAngle, lateralDist, forwardDist, path))
            {
                if (path.IsValid && !IsPathBlocked(path.Segments))
                {
                    return path;
                }
            }
        }

        // 4. Try Manhattan routing (90° turns, predictable)
        path = new RoutedPath();
        RouteManhattan(startX, startY, startAngle, endX, endY, endInputAngle, path);
        if (path.IsValid && !IsPathBlocked(path.Segments))
        {
            return path;
        }

        // 5. Try A* for obstacle avoidance
        if (PathfindingGrid != null)
        {
            var astarPath = new RoutedPath();
            if (TryRouteAStar(startX, startY, startAngle, endX, endY, endInputAngle, astarPath, startPin, endPin))
            {
                if (astarPath.IsValid)
                {
                    return astarPath;
                }
            }
        }

        // Return whatever we have (may be blocked)
        if (!path.IsValid || path.Segments.Count == 0)
        {
            path = new RoutedPath { IsBlockedFallback = true };
            RouteManhattan(startX, startY, startAngle, endX, endY, endInputAngle, path);
        }

        if (IsPathBlocked(path.Segments))
        {
            path.IsBlockedFallback = true;
        }

        return path;
    }

    /// <summary>
    /// Attempts to route with a straight line (only works if pins are aligned).
    /// </summary>
    private bool TryRouteStraight(double startX, double startY, double startAngle,
                                   double endX, double endY, double endInputAngle,
                                   RoutedPath path)
    {
        double dx = endX - startX;
        double dy = endY - startY;
        double connectionAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

        // Check if start pin points toward end
        double startDiff = Math.Abs(NormalizeAngle(connectionAngle - startAngle));
        // Check if end pin can receive from this direction
        double endDiff = Math.Abs(NormalizeAngle(connectionAngle - endInputAngle));

        // Only allow straight routing if pins are truly aligned (< 2° tolerance)
        // This prevents diagonal lines that don't match pin angles
        if (startDiff < 2 && endDiff < 2)
        {
            // Check if path is blocked by obstacles
            if (IsLineBlocked(startX, startY, endX, endY))
            {
                return false;
            }

            path.Segments.Add(new StraightSegment(startX, startY, endX, endY, startAngle));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a straight line between two points passes through any blocked cells.
    /// </summary>
    private bool IsLineBlocked(double x1, double y1, double x2, double y2)
    {
        if (PathfindingGrid == null)
            return false; // No grid, assume not blocked

        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001)
            return false;

        // Normalize direction
        dx /= length;
        dy /= length;

        // Sample points along the line, checking each for blocked cells
        double stepSize = PathfindingGrid.CellSizeMicrometers;
        for (double t = stepSize; t < length - stepSize; t += stepSize)
        {
            double px = x1 + dx * t;
            double py = y1 + dy * t;

            var (gx, gy) = PathfindingGrid.PhysicalToGrid(px, py);
            if (PathfindingGrid.IsBlocked(gx, gy))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Simple, flexible S-bend routing.
    /// Creates smooth curves between two pins without strict Manhattan constraints.
    /// </summary>
    private bool TryRouteSBend(double startX, double startY, double startAngle,
                                double endX, double endY, double endInputAngle,
                                RoutedPath path)
    {
        double r = MinBendRadiusMicrometers;
        double dx = endX - startX;
        double dy = endY - startY;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < 1.0) return false;

        // Direction vectors from start angle
        double startRad = startAngle * Math.PI / 180;
        double fwdX = Math.Cos(startRad);
        double fwdY = Math.Sin(startRad);

        // Project to get forward and lateral distances
        double fwdDist = dx * fwdX + dy * fwdY;

        // Use cross product to determine which side the target is on
        // cross = fwdX * dy - fwdY * dx
        // Positive cross = target is to the LEFT (counter-clockwise from forward)
        // Negative cross = target is to the RIGHT (clockwise from forward)
        double cross = fwdX * dy - fwdY * dx;
        double absLat = Math.Abs(cross) / dist * dist; // Approximate lateral distance

        // Recalculate proper lateral distance
        // Left perpendicular (90° CCW from forward): (-fwdY, fwdX)
        double leftX = -fwdY;
        double leftY = fwdX;
        double latDist = dx * leftX + dy * leftY;
        absLat = Math.Abs(latDist);

        // Target is behind us - can't do simple S-bend
        if (fwdDist < r)
        {
            return false;
        }

        // Very small lateral offset - TryRouteStraight should handle truly aligned pins
        // For small but non-zero offsets, we still need a proper S-bend
        if (absLat < 0.5)
        {
            // Essentially aligned - let straight routing handle it
            return false;
        }

        // S-bend with flexible angle
        // Calculate the angle needed based on geometry
        double sweepAngle;
        if (absLat <= 2 * r)
        {
            // Can achieve with single S-bend
            double cosVal = 1.0 - absLat / (2.0 * r);
            cosVal = Math.Max(-1, Math.Min(1, cosVal));
            sweepAngle = Math.Acos(cosVal) * 180.0 / Math.PI;
        }
        else
        {
            // Need larger angle - use atan for reasonable estimate
            sweepAngle = Math.Atan2(absLat, fwdDist) * 180.0 / Math.PI;
        }

        // Clamp angle to reasonable range
        sweepAngle = Math.Max(5, Math.Min(75, sweepAngle));

        // Determine bend direction based on which side target is
        // latDist > 0 means target is to the LEFT → turn left (positive sweep = CCW)
        // latDist < 0 means target is to the RIGHT → turn right (negative sweep = CW)
        double bendDir = latDist >= 0 ? 1 : -1;

        // Perpendicular pointing toward the target (left if bendDir=1, right if bendDir=-1)
        double perpX = leftX * bendDir;
        double perpY = leftY * bendDir;

        // First bend center is perpendicular to start direction, on the inside of the turn
        double c1x = startX + perpX * r;
        double c1y = startY + perpY * r;
        var bend1 = new BendSegment(c1x, c1y, r, startAngle, sweepAngle * bendDir);
        path.Segments.Add(bend1);

        double x = bend1.EndPoint.X;
        double y = bend1.EndPoint.Y;
        double angle = NormalizeAngle(startAngle + sweepAngle * bendDir);

        // Middle straight section
        double midRad = angle * Math.PI / 180;
        double midFwdX = Math.Cos(midRad);
        double midFwdY = Math.Sin(midRad);

        double dxToEnd = endX - x;
        double dyToEnd = endY - y;
        double fwdToEnd = dxToEnd * midFwdX + dyToEnd * midFwdY;

        if (fwdToEnd > r + 1)
        {
            double straightLen = fwdToEnd - r;
            double sx = x + midFwdX * straightLen;
            double sy = y + midFwdY * straightLen;
            path.Segments.Add(new StraightSegment(x, y, sx, sy, angle));
            x = sx;
            y = sy;
        }

        // Second bend - turn back toward original direction
        // Left perpendicular of mid direction
        double midLeftX = -midFwdY;
        double midLeftY = midFwdX;

        // For second bend, we turn the OPPOSITE way (back to original direction)
        // Center is on the opposite side from first bend
        double c2x = x - midLeftX * bendDir * r;
        double c2y = y - midLeftY * bendDir * r;
        var bend2 = new BendSegment(c2x, c2y, r, angle, -sweepAngle * bendDir);
        path.Segments.Add(bend2);

        x = bend2.EndPoint.X;
        y = bend2.EndPoint.Y;

        // Final straight to exact end position
        double finalDist = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
        if (finalDist > 0.5)
        {
            path.Segments.Add(new StraightSegment(x, y, endX, endY, startAngle));
        }

        // Check for obstacles
        if (IsPathBlocked(path.Segments))
        {
            path.Segments.Clear();
            return false;
        }

        return path.Segments.Count > 0;
    }

    /// <summary>
    /// Checks if any segment in a path passes through blocked cells.
    /// </summary>
    private bool IsPathBlocked(IEnumerable<PathSegment> segments)
    {
        if (PathfindingGrid == null)
            return false;

        foreach (var segment in segments)
        {
            if (segment is StraightSegment)
            {
                if (IsLineBlocked(segment.StartPoint.X, segment.StartPoint.Y,
                                  segment.EndPoint.X, segment.EndPoint.Y))
                {
                    return true;
                }
            }
            else if (segment is BendSegment bend)
            {
                // Check points along the arc
                if (IsArcBlocked(bend))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if an arc segment passes through blocked cells.
    /// </summary>
    private bool IsArcBlocked(BendSegment bend)
    {
        if (PathfindingGrid == null)
            return false;

        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        int numSamples = Math.Max(10, (int)(Math.Abs(bend.SweepAngleDegrees) / 5));

        for (int i = 1; i < numSamples; i++) // Skip start and end points
        {
            double t = (double)i / numSamples;
            double angle = startRad + sweepRad * t;

            double sign = Math.Sign(bend.SweepAngleDegrees);
            if (sign == 0) sign = 1;

            double px = bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle - Math.PI / 2 * sign);
            double py = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle - Math.PI / 2 * sign);

            var (gx, gy) = PathfindingGrid.PhysicalToGrid(px, py);
            if (PathfindingGrid.IsBlocked(gx, gy))
            {
                return true;
            }
        }

        return false;
    }

    private double CalculateSBendSweep(double forward, double lateral, double radius)
    {
        // For an S-bend: lateral = 2 * R * (1 - cos(sweep))
        // cos(sweep) = 1 - lateral / (2 * R)
        double cosValue = 1 - lateral / (2 * radius);
        if (cosValue < -1 || cosValue > 1) return double.NaN;
        return Math.Acos(cosValue) * 180 / Math.PI;
    }

    /// <summary>
    /// Routes a U-turn path when pins face the same direction.
    /// Uses small-angle bends when components are nearly aligned (e.g., 10° instead of 90°).
    /// This avoids the ugly 90°-180°-90° zigzag pattern.
    /// </summary>
    private bool TryRouteUturn(double startX, double startY, double startAngle,
                                double endX, double endY, double endInputAngle,
                                double lateralDist, double forwardDist, RoutedPath path)
    {
        double r = MinBendRadiusMicrometers;
        double absLateral = Math.Abs(lateralDist);

        // Get direction vectors
        double startRad = startAngle * Math.PI / 180;
        double forwardX = Math.Cos(startRad);
        double forwardY = Math.Sin(startRad);
        double perpX = -forwardY; // 90° left of forward
        double perpY = forwardX;

        // Determine which direction to turn (up or down for horizontal pins)
        double bendDirection = lateralDist >= 0 ? 1 : -1;

        // Calculate the minimum bend angle needed to achieve the lateral offset
        // For small lateral offsets, use small angles (like 10-20°)
        // For larger offsets, need larger angles up to 90°
        double minAngle;
        if (absLateral < 0.5)
        {
            // Nearly aligned - use a very small angle
            minAngle = 5;
        }
        else if (absLateral < 2 * r)
        {
            // Calculate angle needed: lateral = 2 * R * (1 - cos(θ))
            // cos(θ) = 1 - lateral / (2 * R)
            double cosValue = 1 - absLateral / (2 * r);
            cosValue = Math.Max(-1, Math.Min(1, cosValue));
            minAngle = Math.Acos(cosValue) * 180 / Math.PI;
        }
        else
        {
            // Lateral offset too large for gentle bend
            minAngle = 90;
        }
        minAngle = Math.Max(5, Math.Min(90, minAngle));

        // For a U-turn, we need total 180° of turning.
        // With small-angle approach:
        // Bend 1: +minAngle (turn toward lateral direction)
        // Bend 2: +(180 - 2*minAngle) (the main reversal)
        // Bend 3: +minAngle (align with end direction)
        // Total: minAngle + (180 - 2*minAngle) + minAngle = 180°

        // For small minAngle (e.g., 10°):
        // Bend 1: +10°, Bend 2: +160°, Bend 3: +10° = 180° total
        // This creates a smooth hairpin turn

        double sweep1 = minAngle * bendDirection;
        double sweep2 = (180 - 2 * minAngle) * bendDirection;
        double sweep3 = minAngle * bendDirection;

        // If sweep2 is too large (> 90°), we need to split it
        // But for now, let's try with the geometry as-is

        // First, we need some forward distance before the turn
        // The total forward travel during the U-turn depends on the bend geometry
        double forwardFromBends = 2 * r * Math.Sin(minAngle * Math.PI / 180);
        double neededForward = Math.Max(r * 2, forwardDist + r * 2);

        // Start with a straight segment forward
        double x = startX;
        double y = startY;
        double angle = startAngle;

        double straightForward = neededForward - forwardFromBends;
        if (straightForward > 0.5)
        {
            double x1 = x + forwardX * straightForward;
            double y1 = y + forwardY * straightForward;
            path.Segments.Add(new StraightSegment(x, y, x1, y1, angle));
            x = x1;
            y = y1;
        }

        // First bend: small turn toward lateral direction
        double perpDir = perpX * bendDirection;
        double perpDirY = perpY * bendDirection;
        double center1X = x + perpDir * r;
        double center1Y = y + perpDirY * r;
        var bend1 = new BendSegment(center1X, center1Y, r, angle, sweep1);
        path.Segments.Add(bend1);
        x = bend1.EndPoint.X;
        y = bend1.EndPoint.Y;
        angle = NormalizeAngle(angle + sweep1);

        // Second bend: the main reversal (may need special handling if > 90°)
        if (Math.Abs(sweep2) > 90)
        {
            // Split into two 90° bends with a straight section between
            // This ensures we stay within the bend radius constraints

            // First half of reversal
            double halfSweep = 90 * bendDirection;
            double midAngleRad = angle * Math.PI / 180;
            double midPerpX = -Math.Sin(midAngleRad) * bendDirection;
            double midPerpY = Math.Cos(midAngleRad) * bendDirection;
            double center2aX = x + midPerpX * r;
            double center2aY = y + midPerpY * r;
            var bend2a = new BendSegment(center2aX, center2aY, r, angle, halfSweep);
            path.Segments.Add(bend2a);
            x = bend2a.EndPoint.X;
            y = bend2a.EndPoint.Y;
            angle = NormalizeAngle(angle + halfSweep);

            // Remaining sweep for second half
            double remainingSweep = sweep2 - halfSweep;
            if (Math.Abs(remainingSweep) > 5)
            {
                midAngleRad = angle * Math.PI / 180;
                midPerpX = -Math.Sin(midAngleRad) * bendDirection;
                midPerpY = Math.Cos(midAngleRad) * bendDirection;
                double center2bX = x + midPerpX * r;
                double center2bY = y + midPerpY * r;
                var bend2b = new BendSegment(center2bX, center2bY, r, angle, remainingSweep);
                path.Segments.Add(bend2b);
                x = bend2b.EndPoint.X;
                y = bend2b.EndPoint.Y;
                angle = NormalizeAngle(angle + remainingSweep);
            }
        }
        else
        {
            // Small enough for single bend
            double midAngleRad = angle * Math.PI / 180;
            double midPerpX = -Math.Sin(midAngleRad) * bendDirection;
            double midPerpY = Math.Cos(midAngleRad) * bendDirection;
            double center2X = x + midPerpX * r;
            double center2Y = y + midPerpY * r;
            var bend2 = new BendSegment(center2X, center2Y, r, angle, sweep2);
            path.Segments.Add(bend2);
            x = bend2.EndPoint.X;
            y = bend2.EndPoint.Y;
            angle = NormalizeAngle(angle + sweep2);
        }

        // Third bend: align with end direction
        if (Math.Abs(sweep3) > 5)
        {
            double finalAngleRad = angle * Math.PI / 180;
            double finalPerpX = -Math.Sin(finalAngleRad) * bendDirection;
            double finalPerpY = Math.Cos(finalAngleRad) * bendDirection;
            double center3X = x + finalPerpX * r;
            double center3Y = y + finalPerpY * r;
            var bend3 = new BendSegment(center3X, center3Y, r, angle, sweep3);
            path.Segments.Add(bend3);
            x = bend3.EndPoint.X;
            y = bend3.EndPoint.Y;
            angle = NormalizeAngle(angle + sweep3);
        }

        // Final straight to end
        double distToEnd = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
        if (distToEnd > 0.5)
        {
            path.Segments.Add(new StraightSegment(x, y, endX, endY, angle));
        }

        return path.Segments.Count > 0 && path.IsValid;
    }

    /// <summary>
    /// Attempts to route using A* pathfinding with obstacle avoidance.
    /// </summary>
    private bool TryRouteAStar(double startX, double startY, double startAngle,
                                double endX, double endY, double endInputAngle,
                                RoutedPath path, PhysicalPin startPin, PhysicalPin endPin)
    {
        if (PathfindingGrid == null)
            return false;

        // Clear corridors at pin locations to ensure path can start/end
        // even if pins are close to component edges
        // Only clears component obstacles, not waveguide obstacles
        double corridorLength = MinBendRadiusMicrometers * 3;
        double corridorWidth = MinWaveguideSpacingMicrometers * 2;

        var clearedStart = PathfindingGrid.ClearPinCorridor(startX, startY, startAngle, corridorLength, corridorWidth);
        var clearedEnd = PathfindingGrid.ClearPinCorridor(endX, endY, endInputAngle, corridorLength, corridorWidth);

        try
        {
            // Convert physical coordinates to grid
            var (gridStartX, gridStartY) = PathfindingGrid.PhysicalToGrid(startX, startY);
            var (gridEndX, gridEndY) = PathfindingGrid.PhysicalToGrid(endX, endY);

            // Convert angles to grid directions
            var startDir = GridDirectionExtensions.FromAngle(startAngle);
            var endDir = GridDirectionExtensions.FromAngle(endInputAngle);

            // Run A* pathfinding with adaptive pin escape distance
            // Try with full escape distance first, then fallback to reduced if blocked
            var pathfinder = new AStarPathfinder.AStarPathfinder(PathfindingGrid, CostCalculator);

            int originalEscapeCells = CostCalculator.MinPinEscapeCells;
            List<AStarPathfinder.AStarNode>? gridPath = null;

            // Attempt 1: Full escape distance (clean exit)
            gridPath = pathfinder.FindPath(gridStartX, gridStartY, startDir,
                                            gridEndX, gridEndY, endDir);

            // Attempt 2: If failed, try with reduced escape distance (tight spaces)
            if (gridPath == null || gridPath.Count < 2)
            {
                CostCalculator.MinPinEscapeCells = Math.Max(5, originalEscapeCells / 3); // 15 -> 5 cells
                gridPath = pathfinder.FindPath(gridStartX, gridStartY, startDir,
                                                gridEndX, gridEndY, endDir);
                CostCalculator.MinPinEscapeCells = originalEscapeCells; // Restore
            }

            if (gridPath == null || gridPath.Count < 2)
                return false;

            // Convert grid path to smooth segments (with foundry-allowed bend radii)
            var smoother = new PathSmoother(PathfindingGrid, MinBendRadiusMicrometers, AllowedBendRadii);
            var smoothedPath = smoother.ConvertToSegments(gridPath, startPin, endPin);

            path.Segments.AddRange(smoothedPath.Segments);

            // Store debug grid path for visualization
            path.DebugGridPath = gridPath;

            return path.Segments.Count > 0;
        }
        finally
        {
            // Restore cleared cells to their original state
            PathfindingGrid.RestoreCells(clearedStart);
            PathfindingGrid.RestoreCells(clearedEnd);
        }
    }

    /// <summary>
    /// Simple geometric Manhattan routing.
    /// Uses only horizontal (0°/180°) and vertical (90°/270°) segments with 90° bends.
    /// Explicitly handles all 16 angle combinations.
    /// </summary>
    private void RouteManhattan(double startX, double startY, double startAngle,
                                 double endX, double endY, double endInputAngle,
                                 RoutedPath path)
    {
        double r = MinBendRadiusMicrometers;

        // Quantize to cardinal directions (0=East, 90=North, 180=West, 270=South)
        int startDir = (int)QuantizeToCardinal(startAngle);
        int endDir = (int)QuantizeToCardinal(endInputAngle);

        // Relative position
        double dx = endX - startX;  // positive = end is to the RIGHT
        double dy = endY - startY;  // positive = end is ABOVE

        // Route based on explicit angle combinations
        // Key insight: there are 16 combinations, but we can handle them systematically
        RouteManhattanExplicit(path, startX, startY, startDir, endX, endY, endDir, dx, dy, r);

        // IMPORTANT: Ensure the path starts and ends EXACTLY at the pin positions.
        // This prevents the pathIsStale check in rendering from triggering a fallback line.
        if (path.Segments.Count > 0)
        {
            // Fix start position
            var firstSeg = path.Segments[0];
            double startDist = Math.Sqrt(Math.Pow(firstSeg.StartPoint.X - startX, 2) +
                                         Math.Pow(firstSeg.StartPoint.Y - startY, 2));
            if (startDist > 0.01)
            {
                if (firstSeg is StraightSegment straight)
                {
                    path.Segments[0] = new StraightSegment(
                        startX, startY,
                        straight.EndPoint.X, straight.EndPoint.Y,
                        straight.StartAngleDegrees);
                }
                else if (firstSeg is BendSegment)
                {
                    // For bend, insert a tiny straight segment before it
                    path.Segments.Insert(0, new StraightSegment(
                        startX, startY,
                        firstSeg.StartPoint.X, firstSeg.StartPoint.Y,
                        startDir)); // Use startDir instead of angle0
                }
            }

            // Fix end position
            var lastSeg = path.Segments[^1];
            double endDist = Math.Sqrt(Math.Pow(lastSeg.EndPoint.X - endX, 2) +
                                       Math.Pow(lastSeg.EndPoint.Y - endY, 2));
            if (endDist > 0.01)
            {
                if (lastSeg is StraightSegment straight)
                {
                    path.Segments[^1] = new StraightSegment(
                        straight.StartPoint.X, straight.StartPoint.Y,
                        endX, endY, straight.StartAngleDegrees);
                }
                else if (lastSeg is BendSegment bend)
                {
                    // For bend, add a tiny straight segment after it
                    path.Segments.Add(new StraightSegment(
                        bend.EndPoint.X, bend.EndPoint.Y,
                        endX, endY, bend.EndAngleDegrees));
                }
            }
        }
    }

    /// <summary>
    /// Explicit routing for all 16 angle combinations.
    /// Each case is handled with clear, predictable logic.
    /// </summary>
    private void RouteManhattanExplicit(RoutedPath path, double x, double y, int startDir,
                                         double endX, double endY, int endDir,
                                         double dx, double dy, double r)
    {
        // startDir/endDir: 0=East, 90=North, 180=West, 270=South
        // dx: positive = end is to the right
        // dy: positive = end is above

        // The basic patterns:
        // - Same direction, target in that direction: S-bend (2 bends)
        // - Same direction, target behind: U-turn (2 bends) + possible extra
        // - Perpendicular: L-shape (1 bend) or Z-shape (2 bends)
        // - Opposite directions: depends on alignment

        switch (startDir)
        {
            case 0: // Start going EAST (right)
                RouteFromEast(path, x, y, endX, endY, endDir, dx, dy, r);
                break;
            case 90: // Start going NORTH (up)
                RouteFromNorth(path, x, y, endX, endY, endDir, dx, dy, r);
                break;
            case 180: // Start going WEST (left)
                RouteFromWest(path, x, y, endX, endY, endDir, dx, dy, r);
                break;
            case 270: // Start going SOUTH (down)
                RouteFromSouth(path, x, y, endX, endY, endDir, dx, dy, r);
                break;
        }
    }

    private void RouteFromEast(RoutedPath path, double x, double y,
                                double endX, double endY, int endDir,
                                double dx, double dy, double r)
    {
        // Starting direction: EAST (going right, angle=0)
        switch (endDir)
        {
            case 0: // End also EAST - S-bend or U-turn
                if (dx > 2 * r) // Target is to the right - simple S-bend
                {
                    // Go right, bend up/down, go right to end
                    double midX = x + dx / 2;
                    path.Segments.Add(new StraightSegment(x, y, midX - r, y, 0));
                    int turnDir = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, midX - r, y, 0, turnDir, r);
                    var bend1End = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend1End.X, bend1End.Y, bend1End.X, endY + (dy >= 0 ? -r : r), turnDir));
                    AddCardinalBend(path, bend1End.X, endY + (dy >= 0 ? -r : r), turnDir, 0, r);
                    var bend2End = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - bend2End.X) > 0.5)
                        path.Segments.Add(new StraightSegment(bend2End.X, bend2End.Y, endX, endY, 0));
                }
                else // Target is behind or very close - U-turn
                {
                    double escape = Math.Max(r, Math.Abs(dx) + r);
                    path.Segments.Add(new StraightSegment(x, y, x + escape, y, 0));
                    int turnDir = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, x + escape, y, 0, turnDir, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double vertDist = Math.Abs(dy) - 2 * r;
                    if (vertDist > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, b1.Y + (dy >= 0 ? vertDist : -vertDist), turnDir));
                    var afterVert = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterVert.X, afterVert.Y, turnDir, 0, r);
                }
                break;

            case 90: // End NORTH - L or Z shape
                if (dx > r && dy > r) // Simple L: go right then up
                {
                    path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
                    AddCardinalBend(path, endX - r, y, 0, 90, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 90));
                }
                else if (dx > r && dy < -r) // Z: right, down, then need to come up
                {
                    path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
                    AddCardinalBend(path, endX - r, y, 0, 270, r);
                    var b1 = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, endY - r, 270));
                    AddCardinalBend(path, b1.X, endY - r, 270, 0, r);
                    var b2 = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, b2.X, b2.Y, 0, 90, r);
                }
                else // Need detour
                {
                    path.Segments.Add(new StraightSegment(x, y, x + r, y, 0));
                    int firstTurn = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, x + r, y, 0, firstTurn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetY = dy >= 0 ? Math.Max(endY + r, b1.Y) : endY - r;
                    if (Math.Abs(targetY - b1.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, targetY, firstTurn));
                    var afterV = path.Segments[^1].EndPoint;
                    int hDir = endX > afterV.X ? 0 : 180;
                    AddCardinalBend(path, afterV.X, afterV.Y, firstTurn, hDir, r);
                    var b2 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b2.X) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX - (hDir == 0 ? r : -r), b2.Y, hDir));
                    var afterH = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterH.X, afterH.Y, hDir, 90, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, endX, endY, 90));
                }
                break;

            case 180: // End WEST - going opposite directions
                // Need to go around
                path.Segments.Add(new StraightSegment(x, y, x + r, y, 0));
                int turn1 = dy >= 0 ? 90 : 270;
                AddCardinalBend(path, x + r, y, 0, turn1, r);
                var p1 = path.Segments[^1].EndPoint;
                double yTarget = dy >= 0 ? Math.Max(endY + r, p1.Y + r) : Math.Min(endY - r, p1.Y - r);
                if (Math.Abs(yTarget - p1.Y) > 0.5)
                    path.Segments.Add(new StraightSegment(p1.X, p1.Y, p1.X, yTarget, turn1));
                var p2 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, p2.X, p2.Y, turn1, 180, r);
                var p3 = path.Segments[^1].EndPoint;
                if (Math.Abs(endX - p3.X) > r + 0.5)
                    path.Segments.Add(new StraightSegment(p3.X, p3.Y, endX + r, p3.Y, 180));
                var p4 = path.Segments[^1].EndPoint;
                int turn2 = endY > p4.Y ? 90 : 270;
                AddCardinalBend(path, p4.X, p4.Y, 180, turn2, r);
                var p5 = path.Segments[^1].EndPoint;
                if (Math.Abs(endY - p5.Y) > r + 0.5)
                    path.Segments.Add(new StraightSegment(p5.X, p5.Y, p5.X, endY + (turn2 == 90 ? -r : r), turn2));
                var p6 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, p6.X, p6.Y, turn2, 180, r);
                break;

            case 270: // End SOUTH - L or Z shape
                if (dx > r && dy < -r) // Simple L: go right then down
                {
                    path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
                    AddCardinalBend(path, endX - r, y, 0, 270, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 270));
                }
                else if (dx > r && dy > r) // Z: right, up, then need to come down
                {
                    path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
                    AddCardinalBend(path, endX - r, y, 0, 90, r);
                    var b1 = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, endY + r, 90));
                    AddCardinalBend(path, b1.X, endY + r, 90, 0, r);
                    var b2 = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, b2.X, b2.Y, 0, 270, r);
                }
                else // Need detour
                {
                    path.Segments.Add(new StraightSegment(x, y, x + r, y, 0));
                    int firstTurn = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, x + r, y, 0, firstTurn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetY = dy >= 0 ? endY + r : Math.Min(endY - r, b1.Y - r);
                    if (Math.Abs(targetY - b1.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, targetY, firstTurn));
                    var afterV = path.Segments[^1].EndPoint;
                    int hDir = endX > afterV.X ? 0 : 180;
                    AddCardinalBend(path, afterV.X, afterV.Y, firstTurn, hDir, r);
                    var b2 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b2.X) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX - (hDir == 0 ? r : -r), b2.Y, hDir));
                    var afterH = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterH.X, afterH.Y, hDir, 270, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, endX, endY, 270));
                }
                break;
        }
    }

    private void RouteFromNorth(RoutedPath path, double x, double y,
                                 double endX, double endY, int endDir,
                                 double dx, double dy, double r)
    {
        // Starting direction: NORTH (going up, angle=90)
        // Mirror of RouteFromEast with x/y and dx/dy swapped
        switch (endDir)
        {
            case 90: // End also NORTH
                if (dy > 2 * r) // Target is above
                {
                    double midY = y + dy / 2;
                    path.Segments.Add(new StraightSegment(x, y, x, midY - r, 90));
                    int turnDir = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, midY - r, 90, turnDir, r);
                    var bend1End = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend1End.X, bend1End.Y, endX + (dx >= 0 ? -r : r), bend1End.Y, turnDir));
                    AddCardinalBend(path, endX + (dx >= 0 ? -r : r), bend1End.Y, turnDir, 90, r);
                    var bend2End = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - bend2End.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(bend2End.X, bend2End.Y, endX, endY, 90));
                }
                else // U-turn needed
                {
                    double escape = Math.Max(r, Math.Abs(dy) + r);
                    path.Segments.Add(new StraightSegment(x, y, x, y + escape, 90));
                    int turnDir = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, y + escape, 90, turnDir, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double horizDist = Math.Abs(dx) - 2 * r;
                    if (horizDist > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X + (dx >= 0 ? horizDist : -horizDist), b1.Y, turnDir));
                    var afterH = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterH.X, afterH.Y, turnDir, 90, r);
                }
                break;

            case 0: // End EAST
                if (dy > r && dx > r)
                {
                    path.Segments.Add(new StraightSegment(x, y, x, endY - r, 90));
                    AddCardinalBend(path, x, endY - r, 90, 0, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 0));
                }
                else
                {
                    path.Segments.Add(new StraightSegment(x, y, x, y + r, 90));
                    int turn = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, y + r, 90, turn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetX = dx >= 0 ? Math.Max(endX - r, b1.X + r) : endX + r;
                    if (Math.Abs(targetX - b1.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, targetX, b1.Y, turn));
                    var b2 = path.Segments[^1].EndPoint;
                    int turn2 = endY > b2.Y ? 90 : 270;
                    AddCardinalBend(path, b2.X, b2.Y, turn, turn2, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, b3.X, endY - (turn2 == 90 ? r : -r), turn2));
                    var b4 = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, b4.X, b4.Y, turn2, 0, r);
                    var b5 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b5.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b5.X, b5.Y, endX, endY, 0));
                }
                break;

            case 180: // End WEST
                if (dy > r && dx < -r)
                {
                    path.Segments.Add(new StraightSegment(x, y, x, endY - r, 90));
                    AddCardinalBend(path, x, endY - r, 90, 180, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 180));
                }
                else
                {
                    path.Segments.Add(new StraightSegment(x, y, x, y + r, 90));
                    int turn = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, y + r, 90, turn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetX = dx >= 0 ? endX - r : Math.Min(endX + r, b1.X - r);
                    if (Math.Abs(targetX - b1.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, targetX, b1.Y, turn));
                    var b2 = path.Segments[^1].EndPoint;
                    int turn2 = endY > b2.Y ? 90 : 270;
                    AddCardinalBend(path, b2.X, b2.Y, turn, turn2, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, b3.X, endY - (turn2 == 90 ? r : -r), turn2));
                    var b4 = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, b4.X, b4.Y, turn2, 180, r);
                    var b5 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b5.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b5.X, b5.Y, endX, endY, 180));
                }
                break;

            case 270: // End SOUTH - opposite
                path.Segments.Add(new StraightSegment(x, y, x, y + r, 90));
                int t1 = dx >= 0 ? 0 : 180;
                AddCardinalBend(path, x, y + r, 90, t1, r);
                var p1 = path.Segments[^1].EndPoint;
                double xTarget = dx >= 0 ? Math.Max(endX + r, p1.X + r) : Math.Min(endX - r, p1.X - r);
                if (Math.Abs(xTarget - p1.X) > 0.5)
                    path.Segments.Add(new StraightSegment(p1.X, p1.Y, xTarget, p1.Y, t1));
                var p2 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, p2.X, p2.Y, t1, 270, r);
                var p3 = path.Segments[^1].EndPoint;
                if (Math.Abs(endY - p3.Y) > r + 0.5)
                    path.Segments.Add(new StraightSegment(p3.X, p3.Y, p3.X, endY + r, 270));
                var p4 = path.Segments[^1].EndPoint;
                int t2 = endX > p4.X ? 0 : 180;
                AddCardinalBend(path, p4.X, p4.Y, 270, t2, r);
                var p5 = path.Segments[^1].EndPoint;
                if (Math.Abs(endX - p5.X) > r + 0.5)
                    path.Segments.Add(new StraightSegment(p5.X, p5.Y, endX + (t2 == 0 ? -r : r), p5.Y, t2));
                var p6 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, p6.X, p6.Y, t2, 270, r);
                break;
        }
    }

    private void RouteFromWest(RoutedPath path, double x, double y,
                                double endX, double endY, int endDir,
                                double dx, double dy, double r)
    {
        // Starting direction: WEST (going left, angle=180)
        // Mirror of RouteFromEast
        switch (endDir)
        {
            case 180: // End also WEST
                if (dx < -2 * r)
                {
                    double midX = x + dx / 2;
                    path.Segments.Add(new StraightSegment(x, y, midX + r, y, 180));
                    int turnDir = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, midX + r, y, 180, turnDir, r);
                    var bend1End = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend1End.X, bend1End.Y, bend1End.X, endY + (dy >= 0 ? -r : r), turnDir));
                    AddCardinalBend(path, bend1End.X, endY + (dy >= 0 ? -r : r), turnDir, 180, r);
                    var bend2End = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - bend2End.X) > 0.5)
                        path.Segments.Add(new StraightSegment(bend2End.X, bend2End.Y, endX, endY, 180));
                }
                else
                {
                    double escape = Math.Max(r, Math.Abs(dx) + r);
                    path.Segments.Add(new StraightSegment(x, y, x - escape, y, 180));
                    int turnDir = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, x - escape, y, 180, turnDir, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double vertDist = Math.Abs(dy) - 2 * r;
                    if (vertDist > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, b1.Y + (dy >= 0 ? vertDist : -vertDist), turnDir));
                    var afterVert = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterVert.X, afterVert.Y, turnDir, 180, r);
                }
                break;

            case 90: // End NORTH
                if (dx < -r && dy > r)
                {
                    path.Segments.Add(new StraightSegment(x, y, endX + r, y, 180));
                    AddCardinalBend(path, endX + r, y, 180, 90, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 90));
                }
                else
                {
                    path.Segments.Add(new StraightSegment(x, y, x - r, y, 180));
                    int firstTurn = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, x - r, y, 180, firstTurn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetY = dy >= 0 ? Math.Max(endY + r, b1.Y + r) : endY - r;
                    if (Math.Abs(targetY - b1.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, targetY, firstTurn));
                    var afterV = path.Segments[^1].EndPoint;
                    int hDir = endX > afterV.X ? 0 : 180;
                    AddCardinalBend(path, afterV.X, afterV.Y, firstTurn, hDir, r);
                    var b2 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b2.X) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX - (hDir == 0 ? r : -r), b2.Y, hDir));
                    var afterH = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterH.X, afterH.Y, hDir, 90, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, endX, endY, 90));
                }
                break;

            case 0: // End EAST - opposite
                path.Segments.Add(new StraightSegment(x, y, x - r, y, 180));
                int turn1 = dy >= 0 ? 90 : 270;
                AddCardinalBend(path, x - r, y, 180, turn1, r);
                var pp1 = path.Segments[^1].EndPoint;
                double yyTarget = dy >= 0 ? Math.Max(endY + r, pp1.Y + r) : Math.Min(endY - r, pp1.Y - r);
                if (Math.Abs(yyTarget - pp1.Y) > 0.5)
                    path.Segments.Add(new StraightSegment(pp1.X, pp1.Y, pp1.X, yyTarget, turn1));
                var pp2 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, pp2.X, pp2.Y, turn1, 0, r);
                var pp3 = path.Segments[^1].EndPoint;
                if (Math.Abs(endX - pp3.X) > r + 0.5)
                    path.Segments.Add(new StraightSegment(pp3.X, pp3.Y, endX - r, pp3.Y, 0));
                var pp4 = path.Segments[^1].EndPoint;
                int turn2w = endY > pp4.Y ? 90 : 270;
                AddCardinalBend(path, pp4.X, pp4.Y, 0, turn2w, r);
                var pp5 = path.Segments[^1].EndPoint;
                if (Math.Abs(endY - pp5.Y) > r + 0.5)
                    path.Segments.Add(new StraightSegment(pp5.X, pp5.Y, pp5.X, endY + (turn2w == 90 ? -r : r), turn2w));
                var pp6 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, pp6.X, pp6.Y, turn2w, 0, r);
                break;

            case 270: // End SOUTH
                if (dx < -r && dy < -r)
                {
                    path.Segments.Add(new StraightSegment(x, y, endX + r, y, 180));
                    AddCardinalBend(path, endX + r, y, 180, 270, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 270));
                }
                else
                {
                    path.Segments.Add(new StraightSegment(x, y, x - r, y, 180));
                    int firstTurn = dy >= 0 ? 90 : 270;
                    AddCardinalBend(path, x - r, y, 180, firstTurn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetY = dy >= 0 ? endY + r : Math.Min(endY - r, b1.Y - r);
                    if (Math.Abs(targetY - b1.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, targetY, firstTurn));
                    var afterV = path.Segments[^1].EndPoint;
                    int hDir = endX > afterV.X ? 0 : 180;
                    AddCardinalBend(path, afterV.X, afterV.Y, firstTurn, hDir, r);
                    var b2 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b2.X) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX - (hDir == 0 ? r : -r), b2.Y, hDir));
                    var afterH = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterH.X, afterH.Y, hDir, 270, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, endX, endY, 270));
                }
                break;
        }
    }

    private void RouteFromSouth(RoutedPath path, double x, double y,
                                 double endX, double endY, int endDir,
                                 double dx, double dy, double r)
    {
        // Starting direction: SOUTH (going down, angle=270)
        // Mirror of RouteFromNorth
        switch (endDir)
        {
            case 270: // End also SOUTH
                if (dy < -2 * r)
                {
                    double midY = y + dy / 2;
                    path.Segments.Add(new StraightSegment(x, y, x, midY + r, 270));
                    int turnDir = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, midY + r, 270, turnDir, r);
                    var bend1End = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend1End.X, bend1End.Y, endX + (dx >= 0 ? -r : r), bend1End.Y, turnDir));
                    AddCardinalBend(path, endX + (dx >= 0 ? -r : r), bend1End.Y, turnDir, 270, r);
                    var bend2End = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - bend2End.Y) > 0.5)
                        path.Segments.Add(new StraightSegment(bend2End.X, bend2End.Y, endX, endY, 270));
                }
                else
                {
                    double escape = Math.Max(r, Math.Abs(dy) + r);
                    path.Segments.Add(new StraightSegment(x, y, x, y - escape, 270));
                    int turnDir = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, y - escape, 270, turnDir, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double horizDist = Math.Abs(dx) - 2 * r;
                    if (horizDist > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X + (dx >= 0 ? horizDist : -horizDist), b1.Y, turnDir));
                    var afterH = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, afterH.X, afterH.Y, turnDir, 270, r);
                }
                break;

            case 0: // End EAST
                if (dy < -r && dx > r)
                {
                    path.Segments.Add(new StraightSegment(x, y, x, endY + r, 270));
                    AddCardinalBend(path, x, endY + r, 270, 0, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 0));
                }
                else
                {
                    path.Segments.Add(new StraightSegment(x, y, x, y - r, 270));
                    int turn = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, y - r, 270, turn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetX = dx >= 0 ? Math.Max(endX - r, b1.X + r) : endX + r;
                    if (Math.Abs(targetX - b1.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, targetX, b1.Y, turn));
                    var b2 = path.Segments[^1].EndPoint;
                    int turn2 = endY > b2.Y ? 90 : 270;
                    AddCardinalBend(path, b2.X, b2.Y, turn, turn2, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, b3.X, endY + (turn2 == 90 ? -r : r), turn2));
                    var b4 = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, b4.X, b4.Y, turn2, 0, r);
                    var b5 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b5.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b5.X, b5.Y, endX, endY, 0));
                }
                break;

            case 180: // End WEST
                if (dy < -r && dx < -r)
                {
                    path.Segments.Add(new StraightSegment(x, y, x, endY + r, 270));
                    AddCardinalBend(path, x, endY + r, 270, 180, r);
                    var bend = path.Segments[^1].EndPoint;
                    path.Segments.Add(new StraightSegment(bend.X, bend.Y, endX, endY, 180));
                }
                else
                {
                    path.Segments.Add(new StraightSegment(x, y, x, y - r, 270));
                    int turn = dx >= 0 ? 0 : 180;
                    AddCardinalBend(path, x, y - r, 270, turn, r);
                    var b1 = path.Segments[^1].EndPoint;
                    double targetX = dx >= 0 ? endX - r : Math.Min(endX + r, b1.X - r);
                    if (Math.Abs(targetX - b1.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b1.X, b1.Y, targetX, b1.Y, turn));
                    var b2 = path.Segments[^1].EndPoint;
                    int turn2 = endY > b2.Y ? 90 : 270;
                    AddCardinalBend(path, b2.X, b2.Y, turn, turn2, r);
                    var b3 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endY - b3.Y) > r + 0.5)
                        path.Segments.Add(new StraightSegment(b3.X, b3.Y, b3.X, endY + (turn2 == 90 ? -r : r), turn2));
                    var b4 = path.Segments[^1].EndPoint;
                    AddCardinalBend(path, b4.X, b4.Y, turn2, 180, r);
                    var b5 = path.Segments[^1].EndPoint;
                    if (Math.Abs(endX - b5.X) > 0.5)
                        path.Segments.Add(new StraightSegment(b5.X, b5.Y, endX, endY, 180));
                }
                break;

            case 90: // End NORTH - opposite
                path.Segments.Add(new StraightSegment(x, y, x, y - r, 270));
                int t1s = dx >= 0 ? 0 : 180;
                AddCardinalBend(path, x, y - r, 270, t1s, r);
                var ps1 = path.Segments[^1].EndPoint;
                double xTargetS = dx >= 0 ? Math.Max(endX + r, ps1.X + r) : Math.Min(endX - r, ps1.X - r);
                if (Math.Abs(xTargetS - ps1.X) > 0.5)
                    path.Segments.Add(new StraightSegment(ps1.X, ps1.Y, xTargetS, ps1.Y, t1s));
                var ps2 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, ps2.X, ps2.Y, t1s, 90, r);
                var ps3 = path.Segments[^1].EndPoint;
                if (Math.Abs(endY - ps3.Y) > r + 0.5)
                    path.Segments.Add(new StraightSegment(ps3.X, ps3.Y, ps3.X, endY - r, 90));
                var ps4 = path.Segments[^1].EndPoint;
                int t2s = endX > ps4.X ? 0 : 180;
                AddCardinalBend(path, ps4.X, ps4.Y, 90, t2s, r);
                var ps5 = path.Segments[^1].EndPoint;
                if (Math.Abs(endX - ps5.X) > r + 0.5)
                    path.Segments.Add(new StraightSegment(ps5.X, ps5.Y, endX + (t2s == 0 ? -r : r), ps5.Y, t2s));
                var ps6 = path.Segments[^1].EndPoint;
                AddCardinalBend(path, ps6.X, ps6.Y, t2s, 90, r);
                break;
        }
    }

    // Keep old methods for backward compatibility / reference - can be removed later
    /// <summary>
    /// Route from horizontal start to horizontal end (S-bend or U-turn pattern).
    /// </summary>
    [Obsolete("Use RouteManhattanExplicit instead")]
    private void RouteHorizontalToHorizontal(RoutedPath path, ref double x, ref double y, ref double angle,
                                              double endX, double endY, double startAngle, double endAngle, double r)
    {
        double dx = endX - x;
        double dy = endY - y;
        double startDir = startAngle == 0 ? 1 : -1;  // +1 for East, -1 for West
        double endDir = endAngle == 0 ? 1 : -1;

        // First: go straight in start direction
        double firstStraight = Math.Max(r, Math.Abs(dx) / 2);
        if (startDir * dx < 0)
        {
            // Going away from target - need U-turn, go at least 2*r
            firstStraight = 2 * r;
        }

        double x1 = x + startDir * firstStraight;
        if (firstStraight > 0.5)
        {
            path.Segments.Add(new StraightSegment(x, y, x1, y, angle));
            x = x1;
        }

        // Turn toward the end Y
        double turnAngle = dy > 0 ? 90 : 270;
        AddCardinalBend(path, x, y, angle, turnAngle, r);
        x = path.Segments[^1].EndPoint.X;
        y = path.Segments[^1].EndPoint.Y;
        angle = turnAngle;

        // Go vertically to align with end Y (leave room for final bend)
        double vertDist = Math.Abs(endY - y) - r;
        if (vertDist > 0.5)
        {
            double yDir = dy > 0 ? 1 : -1;
            double y2 = y + yDir * vertDist;
            path.Segments.Add(new StraightSegment(x, y, x, y2, angle));
            y = y2;
        }

        // Turn to match end direction
        AddCardinalBend(path, x, y, angle, endAngle, r);
        x = path.Segments[^1].EndPoint.X;
        y = path.Segments[^1].EndPoint.Y;
        angle = endAngle;
    }

    /// <summary>
    /// Route from vertical start to vertical end (S-bend or U-turn pattern).
    /// </summary>
    private void RouteVerticalToVertical(RoutedPath path, ref double x, ref double y, ref double angle,
                                          double endX, double endY, double startAngle, double endAngle, double r)
    {
        double dx = endX - x;
        double dy = endY - y;
        double startDir = startAngle == 90 ? 1 : -1;  // +1 for North, -1 for South
        double endDir = endAngle == 90 ? 1 : -1;

        // First: go straight in start direction
        double firstStraight = Math.Max(r, Math.Abs(dy) / 2);
        if (startDir * dy < 0)
        {
            // Going away from target - need U-turn
            firstStraight = 2 * r;
        }

        double y1 = y + startDir * firstStraight;
        if (firstStraight > 0.5)
        {
            path.Segments.Add(new StraightSegment(x, y, x, y1, angle));
            y = y1;
        }

        // Turn toward the end X
        double turnAngle = dx > 0 ? 0 : 180;
        AddCardinalBend(path, x, y, angle, turnAngle, r);
        x = path.Segments[^1].EndPoint.X;
        y = path.Segments[^1].EndPoint.Y;
        angle = turnAngle;

        // Go horizontally to align with end X (leave room for final bend)
        double horizDist = Math.Abs(endX - x) - r;
        if (horizDist > 0.5)
        {
            double xDir = dx > 0 ? 1 : -1;
            double x2 = x + xDir * horizDist;
            path.Segments.Add(new StraightSegment(x, y, x2, y, angle));
            x = x2;
        }

        // Turn to match end direction
        AddCardinalBend(path, x, y, angle, endAngle, r);
        x = path.Segments[^1].EndPoint.X;
        y = path.Segments[^1].EndPoint.Y;
        angle = endAngle;
    }

    /// <summary>
    /// Route from horizontal start to vertical end (L-shape).
    /// </summary>
    private void RouteHorizontalToVertical(RoutedPath path, ref double x, ref double y, ref double angle,
                                            double endX, double endY, double startAngle, double endAngle, double r)
    {
        double dx = endX - x;
        double dy = endY - y;
        double startDir = startAngle == 0 ? 1 : -1;

        // Check if we're going toward or away from the end X
        bool goingToward = (startDir * dx) > 0;

        if (goingToward)
        {
            // Simple L-shape: go horizontal, then turn vertical
            double horizDist = Math.Abs(dx) - r;
            if (horizDist > 0.5)
            {
                double x1 = x + startDir * horizDist;
                path.Segments.Add(new StraightSegment(x, y, x1, y, angle));
                x = x1;
            }

            // Turn to vertical
            double turnAngle = dy > 0 ? 90 : 270;
            AddCardinalBend(path, x, y, angle, turnAngle, r);
            x = path.Segments[^1].EndPoint.X;
            y = path.Segments[^1].EndPoint.Y;
            angle = turnAngle;

            // Go vertical (leave room for end angle bend if needed)
            double vertDist = Math.Abs(endY - y);
            if (angle != endAngle)
            {
                vertDist -= r;
            }
            if (vertDist > 0.5)
            {
                double yDir = dy > 0 ? 1 : -1;
                double y2 = y + yDir * vertDist;
                path.Segments.Add(new StraightSegment(x, y, x, y2, angle));
                y = y2;
            }

            // Final bend if needed to match end angle
            if (Math.Abs(NormalizeAngle(angle - endAngle)) > 10)
            {
                AddCardinalBend(path, x, y, angle, endAngle, r);
                x = path.Segments[^1].EndPoint.X;
                y = path.Segments[^1].EndPoint.Y;
                angle = endAngle;
            }
        }
        else
        {
            // Going away - need to make a U-turn first
            // Go straight a bit, turn, go past the target, turn back
            double firstStraight = 2 * r;
            path.Segments.Add(new StraightSegment(x, y, x + startDir * firstStraight, y, angle));
            x += startDir * firstStraight;

            // Turn toward end Y
            double turnAngle = dy > 0 ? 90 : 270;
            AddCardinalBend(path, x, y, angle, turnAngle, r);
            x = path.Segments[^1].EndPoint.X;
            y = path.Segments[^1].EndPoint.Y;
            angle = turnAngle;

            // Go vertical
            double vertDist = Math.Abs(endY - y) - r;
            if (vertDist > 0.5)
            {
                double yDir = dy > 0 ? 1 : -1;
                path.Segments.Add(new StraightSegment(x, y, x, y + yDir * vertDist, angle));
                y += yDir * vertDist;
            }

            // Turn to end angle
            AddCardinalBend(path, x, y, angle, endAngle, r);
            x = path.Segments[^1].EndPoint.X;
            y = path.Segments[^1].EndPoint.Y;
            angle = endAngle;
        }
    }

    /// <summary>
    /// Route from vertical start to horizontal end (L-shape).
    /// </summary>
    private void RouteVerticalToHorizontal(RoutedPath path, ref double x, ref double y, ref double angle,
                                            double endX, double endY, double startAngle, double endAngle, double r)
    {
        double dx = endX - x;
        double dy = endY - y;
        double startDir = startAngle == 90 ? 1 : -1;

        // Check if we're going toward or away from the end Y
        bool goingToward = (startDir * dy) > 0;

        if (goingToward)
        {
            // Simple L-shape: go vertical, then turn horizontal
            double vertDist = Math.Abs(dy) - r;
            if (vertDist > 0.5)
            {
                double y1 = y + startDir * vertDist;
                path.Segments.Add(new StraightSegment(x, y, x, y1, angle));
                y = y1;
            }

            // Turn to horizontal
            double turnAngle = dx > 0 ? 0 : 180;
            AddCardinalBend(path, x, y, angle, turnAngle, r);
            x = path.Segments[^1].EndPoint.X;
            y = path.Segments[^1].EndPoint.Y;
            angle = turnAngle;

            // Go horizontal (leave room for end angle bend if needed)
            double horizDist = Math.Abs(endX - x);
            if (angle != endAngle)
            {
                horizDist -= r;
            }
            if (horizDist > 0.5)
            {
                double xDir = dx > 0 ? 1 : -1;
                double x2 = x + xDir * horizDist;
                path.Segments.Add(new StraightSegment(x, y, x2, y, angle));
                x = x2;
            }

            // Final bend if needed to match end angle
            if (Math.Abs(NormalizeAngle(angle - endAngle)) > 10)
            {
                AddCardinalBend(path, x, y, angle, endAngle, r);
                x = path.Segments[^1].EndPoint.X;
                y = path.Segments[^1].EndPoint.Y;
                angle = endAngle;
            }
        }
        else
        {
            // Going away - need to make a detour
            double firstStraight = 2 * r;
            path.Segments.Add(new StraightSegment(x, y, x, y + startDir * firstStraight, angle));
            y += startDir * firstStraight;

            // Turn toward end X
            double turnAngle = dx > 0 ? 0 : 180;
            AddCardinalBend(path, x, y, angle, turnAngle, r);
            x = path.Segments[^1].EndPoint.X;
            y = path.Segments[^1].EndPoint.Y;
            angle = turnAngle;

            // Go horizontal
            double horizDist = Math.Abs(endX - x) - r;
            if (horizDist > 0.5)
            {
                double xDir = dx > 0 ? 1 : -1;
                path.Segments.Add(new StraightSegment(x, y, x + xDir * horizDist, y, angle));
                x += xDir * horizDist;
            }

            // Turn to end angle
            AddCardinalBend(path, x, y, angle, endAngle, r);
            x = path.Segments[^1].EndPoint.X;
            y = path.Segments[^1].EndPoint.Y;
            angle = endAngle;
        }
    }

    /// <summary>
    /// Converts a cardinal angle to a unit direction vector.
    /// </summary>
    private static (double dx, double dy) AngleToDirection(double angle)
    {
        return angle switch
        {
            0 => (1, 0),      // East
            90 => (0, 1),     // North
            180 => (-1, 0),   // West
            270 => (0, -1),   // South
            _ => (Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180))
        };
    }

    /// <summary>
    /// Quantizes an angle to the nearest cardinal direction (0°, 90°, 180°, 270°).
    /// </summary>
    private static double QuantizeToCardinal(double angle)
    {
        angle = NormalizeAngle(angle);
        if (angle >= -45 && angle < 45) return 0;      // East
        if (angle >= 45 && angle < 135) return 90;     // North
        if (angle >= 135 || angle < -135) return 180;  // West
        return 270;                                      // South
    }

    /// <summary>
    /// Checks if a cardinal angle is horizontal (0° or 180°).
    /// </summary>
    private static bool IsCardinalHorizontal(double angle)
    {
        return Math.Abs(angle) < 10 || Math.Abs(angle - 180) < 10 || Math.Abs(angle + 180) < 10;
    }

    /// <summary>
    /// Adds a 90° bend between two cardinal directions.
    /// </summary>
    private void AddCardinalBend(RoutedPath path, double x, double y,
                                  double startAngle, double endAngle, double radius)
    {
        double sweepAngle = NormalizeAngle(endAngle - startAngle);

        // Clamp to 90 degrees
        if (Math.Abs(sweepAngle) > 90)
        {
            sweepAngle = Math.Sign(sweepAngle) * 90;
        }

        double bendDirection = Math.Sign(sweepAngle);
        if (bendDirection == 0) bendDirection = 1;

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

/// <summary>
/// Represents a routed path consisting of multiple segments.
/// </summary>
public class RoutedPath
{
    public List<PathSegment> Segments { get; } = new();

    /// <summary>
    /// Indicates if this path was created as a fallback because no valid path could be found.
    /// When true, the path may pass through obstacles and should be displayed differently (e.g., red/dashed).
    /// </summary>
    public bool IsBlockedFallback { get; set; } = false;

    /// <summary>
    /// Debug information: The raw A* grid path (list of grid nodes) used to generate this path.
    /// Only populated when A* routing is used. Null for other routing strategies.
    /// </summary>
    public List<AStarPathfinder.AStarNode>? DebugGridPath { get; set; } = null;

    /// <summary>
    /// Total length of the path in micrometers.
    /// </summary>
    public double TotalLengthMicrometers => Segments.Sum(s => s.LengthMicrometers);

    /// <summary>
    /// Total equivalent 90-degree bends in the path.
    /// </summary>
    public double TotalEquivalent90DegreeBends => Segments
        .OfType<BendSegment>()
        .Sum(b => b.Equivalent90DegreeBends);

    /// <summary>
    /// Checks if the path is valid (segments connect properly).
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (Segments.Count == 0) return false;
            for (int i = 1; i < Segments.Count; i++)
            {
                var prev = Segments[i - 1];
                var curr = Segments[i];
                double dist = Math.Sqrt(Math.Pow(curr.StartPoint.X - prev.EndPoint.X, 2) +
                                        Math.Pow(curr.StartPoint.Y - prev.EndPoint.Y, 2));
                if (dist > 0.1) return false; // Gap in path
            }
            return true;
        }
    }
}

/// <summary>
/// Represents an obstacle that waveguides must route around.
/// </summary>
public class RoutingObstacle
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Component? SourceComponent { get; set; }

    public bool Contains(double px, double py)
    {
        return px >= X && px <= X + Width && py >= Y && py <= Y + Height;
    }

    public bool Intersects(double x1, double y1, double x2, double y2)
    {
        // Simple line-rectangle intersection check
        // Check if line segment intersects any edge of the rectangle
        return LineIntersectsRect(x1, y1, x2, y2, X, Y, Width, Height);
    }

    private static bool LineIntersectsRect(double x1, double y1, double x2, double y2,
                                            double rx, double ry, double rw, double rh)
    {
        // Check all four edges
        return LineIntersectsLine(x1, y1, x2, y2, rx, ry, rx + rw, ry) ||
               LineIntersectsLine(x1, y1, x2, y2, rx + rw, ry, rx + rw, ry + rh) ||
               LineIntersectsLine(x1, y1, x2, y2, rx, ry + rh, rx + rw, ry + rh) ||
               LineIntersectsLine(x1, y1, x2, y2, rx, ry, rx, ry + rh) ||
               (x1 >= rx && x1 <= rx + rw && y1 >= ry && y1 <= ry + rh); // Start inside
    }

    private static bool LineIntersectsLine(double x1, double y1, double x2, double y2,
                                            double x3, double y3, double x4, double y4)
    {
        double d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(d) < 1e-10) return false;

        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d;
        double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / d;

        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}
