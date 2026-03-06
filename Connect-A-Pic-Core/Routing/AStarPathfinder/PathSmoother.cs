using CAP_Core.Components;
using CAP_Core.Routing;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Converts A* grid paths to physical waveguide segments with proper geometric approach to pins.
/// No correction segments - all connections solved geometrically.
/// </summary>
public class PathSmoother
{
    private readonly PathfindingGrid _grid;
    private readonly double _minBendRadius;
    private readonly BendBuilder _bendBuilder;
    private readonly SBendBuilder _sBendBuilder;

    public PathSmoother(PathfindingGrid grid, double minBendRadius, List<double>? allowedRadii = null)
    {
        _grid = grid;
        _minBendRadius = minBendRadius;
        _bendBuilder = new BendBuilder(minBendRadius, allowedRadii);
        _sBendBuilder = new SBendBuilder(_bendBuilder, minBendRadius);
    }

    /// <summary>
    /// Converts an A* grid path to routed segments with geometric terminal approach.
    /// </summary>
    public RoutedPath ConvertToSegments(List<AStarNode> gridPath, PhysicalPin startPin, PhysicalPin endPin)
    {
        var routedPath = new RoutedPath();

        if (gridPath == null || gridPath.Count < 2)
            return routedPath;

        // Get pin positions and directions
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double currentAngle = AngleUtilities.QuantizeToCardinal(startPin.GetAbsoluteAngle());
        double endEntryAngle = AngleUtilities.NormalizeAngle(endPin.GetAbsoluteAngle() + 180);
        endEntryAngle = AngleUtilities.QuantizeToCardinal(endEntryAngle);

        // Current position
        double x = startX;
        double y = startY;

        // Extract corners where direction changes
        var corners = ExtractCorners(gridPath);
        int lastTurnIndex = FindLastTurningCorner(corners, currentAngle);

        // Process Manhattan routing through corners
        for (int i = 1; i < corners.Count; i++)
        {
            var (cornerX, cornerY) = _grid.GridToPhysical(corners[i].X, corners[i].Y);
            var newDirection = corners[i].Direction;
            double newAngle = AngleUtilities.DirectionToAngle(newDirection);
            bool willTurn = !AngleUtilities.IsAngleClose(currentAngle, newAngle);

            // Snap last turn corner to pin axis for precise alignment
            if (i == lastTurnIndex)
            {
                if (newAngle == 0 || newAngle == 180)
                    cornerY = endY;
                else if (newAngle == 90 || newAngle == 270)
                    cornerX = endX;
            }

            // Calculate distance to corner
            double dx = cornerX - x;
            double dy = cornerY - y;
            bool isHorizontal = (currentAngle == 0 || currentAngle == 180);
            bool isVertical = (currentAngle == 90 || currentAngle == 270);
            double projectedDistance = isHorizontal ? Math.Abs(dx) : Math.Abs(dy);

            // Skip if corner is behind us
            double angleRad = currentAngle * Math.PI / 180;
            double dot = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
            if (dot < -0.01 && !willTurn)
                continue;

            // Add straight segment toward corner
            double straightDistance = willTurn
                ? Math.Max(0, projectedDistance - _minBendRadius)
                : projectedDistance;

            double minSegmentLength = Math.Max(0.5, _grid.CellSizeMicrometers * 0.5);
            if (straightDistance > minSegmentLength && dot > 0.01)
            {
                double dirSign = isHorizontal ? (dx > 0 ? 1 : -1) : (dy > 0 ? 1 : -1);
                double endStraightX = isHorizontal ? x + dirSign * straightDistance : x;
                double endStraightY = isVertical ? y + dirSign * straightDistance : y;

                routedPath.Segments.Add(new StraightSegment(x, y, endStraightX, endStraightY, currentAngle));
                x = endStraightX;
                y = endStraightY;
            }

            // Add bend at corner
            if (willTurn)
            {
                var bend = _bendBuilder.BuildBend(x, y, currentAngle, newAngle, BendMode.Cardinal90);
                if (bend != null)
                {
                    routedPath.Segments.Add(bend);
                    x = bend.EndPoint.X;
                    y = bend.EndPoint.Y;
                }
                currentAngle = newAngle;
            }
        }

        // GEOMETRIC TERMINAL APPROACH - NO CORRECTION SEGMENTS
        AppendTerminalApproach(routedPath, ref x, ref y, ref currentAngle, endX, endY, endEntryAngle);

        return routedPath;
    }

    /// <summary>
    /// Geometrically connects current position to end pin.
    /// Strategies: (1) Direct straight, (2) Two-bend approach, (3) S-bend fallback.
    /// NO correction segments allowed.
    /// </summary>
    private void AppendTerminalApproach(
        RoutedPath path,
        ref double x,
        ref double y,
        ref double currentAngle,
        double endX,
        double endY,
        double endEntryAngle)
    {
        double dx = endX - x;
        double dy = endY - y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        // Already at pin
        if (distance < 0.1)
            return;

        // Calculate lateral offset (perpendicular to current direction)
        double lateralOffset = CalculateLateralOffset(x, y, currentAngle, endX, endY);
        double forwardDistance = CalculateForwardDistance(x, y, currentAngle, endX, endY);

        // STRATEGY 1: Direct straight connection
        if (Math.Abs(lateralOffset) < 0.5 && AngleUtilities.IsAngleClose(currentAngle, endEntryAngle))
        {
            if (forwardDistance > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, currentAngle));
                x = endX;
                y = endY;
            }
            return;
        }

        // STRATEGY 2: Two-bend geometric approach
        if (TryTwoBendApproach(path, ref x, ref y, ref currentAngle, endX, endY, endEntryAngle, lateralOffset, forwardDistance))
            return;

        // STRATEGY 3: S-bend fallback for very tight spacing
        bool sBendSuccess = _sBendBuilder.TryBuildApproachSBend(
            path, ref x, ref y, ref currentAngle,
            endX, endY, endEntryAngle);

        if (!sBendSuccess)
        {
            // Last resort: direct connection (may violate entry angle)
            if (Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2)) > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, currentAngle));
                x = endX;
                y = endY;
            }
        }
    }

    /// <summary>
    /// Two-bend approach: creates smooth geometry to reach pin with correct entry angle.
    /// Bend angles calculated from offset distance.
    /// </summary>
    private bool TryTwoBendApproach(
        RoutedPath path,
        ref double x,
        ref double y,
        ref double currentAngle,
        double endX,
        double endY,
        double endEntryAngle,
        double lateralOffset,
        double forwardDistance)
    {
        // Need minimum forward distance for two bends
        if (forwardDistance < _minBendRadius * 2)
            return false;

        // Calculate bend angle from offset (user's idea: offset/4 per bend)
        // For symmetric S-approach: each bend deflects by arctan(offset / forward_distance)
        double totalAngleChange = AngleUtilities.NormalizeAngle(endEntryAngle - currentAngle);

        // If we need to turn AND have lateral offset, use calculated smooth bends
        if (Math.Abs(lateralOffset) > 0.5)
        {
            // Calculate smooth bend angle based on geometry
            // For gentle approach: angle ≈ atan(lateral_offset / (forward_distance / 2))
            double bendAngle = Math.Atan(Math.Abs(lateralOffset) / (forwardDistance * 0.5)) * 180 / Math.PI;
            bendAngle = Math.Clamp(bendAngle, 5, 45); // Reasonable limits

            // Determine bend direction
            double bendSign = Math.Sign(lateralOffset);

            // First bend: turn toward pin
            double firstBendTarget = currentAngle + bendAngle * bendSign;
            var bend1 = _bendBuilder.BuildBend(x, y, currentAngle, firstBendTarget, BendMode.Flexible);
            if (bend1 != null)
            {
                path.Segments.Add(bend1);
                x = bend1.EndPoint.X;
                y = bend1.EndPoint.Y;
                currentAngle = firstBendTarget;
            }

            // Middle straight section
            double midLength = forwardDistance * 0.4;
            if (midLength > 0.5)
            {
                double midAngleRad = currentAngle * Math.PI / 180;
                double midEndX = x + midLength * Math.Cos(midAngleRad);
                double midEndY = y + midLength * Math.Sin(midAngleRad);
                path.Segments.Add(new StraightSegment(x, y, midEndX, midEndY, currentAngle));
                x = midEndX;
                y = midEndY;
            }

            // Second bend: align with entry angle
            var bend2 = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible);
            if (bend2 != null)
            {
                path.Segments.Add(bend2);
                x = bend2.EndPoint.X;
                y = bend2.EndPoint.Y;
                currentAngle = endEntryAngle;
            }

            // Final straight to pin
            double remaining = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
            if (remaining > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }

            return true;
        }

        // No lateral offset but need to turn - single bend approach
        if (Math.Abs(totalAngleChange) > 5 && forwardDistance > _minBendRadius * 1.5)
        {
            // Go partway straight, then bend to final angle
            double straightDist = forwardDistance - _minBendRadius;
            if (straightDist > 0.5)
            {
                double angleRad = currentAngle * Math.PI / 180;
                double straightEndX = x + straightDist * Math.Cos(angleRad);
                double straightEndY = y + straightDist * Math.Sin(angleRad);
                path.Segments.Add(new StraightSegment(x, y, straightEndX, straightEndY, currentAngle));
                x = straightEndX;
                y = straightEndY;
            }

            var bend = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible);
            if (bend != null)
            {
                path.Segments.Add(bend);
                x = bend.EndPoint.X;
                y = bend.EndPoint.Y;
                currentAngle = endEntryAngle;
            }

            // Final straight to pin
            double remaining = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
            if (remaining > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculate perpendicular offset from current position to end pin.
    /// </summary>
    private static double CalculateLateralOffset(double x, double y, double angle, double endX, double endY)
    {
        double dx = endX - x;
        double dy = endY - y;
        double angleRad = angle * Math.PI / 180;

        // Perpendicular component (cross product in 2D)
        return -dx * Math.Sin(angleRad) + dy * Math.Cos(angleRad);
    }

    /// <summary>
    /// Calculate forward distance along current direction to end pin.
    /// </summary>
    private static double CalculateForwardDistance(double x, double y, double angle, double endX, double endY)
    {
        double dx = endX - x;
        double dy = endY - y;
        double angleRad = angle * Math.PI / 180;

        // Forward component (dot product)
        return dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
    }

    /// <summary>
    /// Finds the index of the last corner with a direction change.
    /// </summary>
    private static int FindLastTurningCorner(
        List<(int X, int Y, GridDirection Direction)> corners, double startAngle)
    {
        int lastTurn = -1;
        double prevAngle = startAngle;
        for (int i = 1; i < corners.Count; i++)
        {
            double angle = AngleUtilities.DirectionToAngle(corners[i].Direction);
            if (!AngleUtilities.IsAngleClose(prevAngle, angle))
                lastTurn = i;
            prevAngle = angle;
        }
        return lastTurn;
    }

    /// <summary>
    /// Extracts corner points where the A* path changes direction.
    /// </summary>
    private List<(int X, int Y, GridDirection Direction)> ExtractCorners(List<AStarNode> gridPath)
    {
        var corners = new List<(int, int, GridDirection)>();

        if (gridPath.Count == 0)
            return corners;

        corners.Add((gridPath[0].X, gridPath[0].Y, gridPath[0].Direction));

        for (int i = 1; i < gridPath.Count; i++)
        {
            var current = gridPath[i];
            var previous = gridPath[i - 1];

            if (current.Direction != previous.Direction)
                corners.Add((current.X, current.Y, current.Direction));
        }

        var lastNode = gridPath[^1];
        if (corners.Count == 0 || corners[^1] != (lastNode.X, lastNode.Y, lastNode.Direction))
            corners.Add((lastNode.X, lastNode.Y, lastNode.Direction));

        return corners;
    }
}
