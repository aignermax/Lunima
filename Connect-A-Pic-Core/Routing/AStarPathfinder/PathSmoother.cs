using CAP_Core.Components;
using CAP_Core.Routing;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Converts A* grid paths to physical waveguide segments.
/// STRICT INVARIANT: All segments must have valid geometry - no exceptions.
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

    public RoutedPath ConvertToSegments(List<AStarNode> gridPath, PhysicalPin startPin, PhysicalPin endPin)
    {
        var routedPath = new RoutedPath();

        if (gridPath == null || gridPath.Count < 2)
            return routedPath;

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double currentAngle = AngleUtilities.QuantizeToCardinal(startPin.GetAbsoluteAngle());
        double endEntryAngle = AngleUtilities.NormalizeAngle(endPin.GetAbsoluteAngle() + 180);
        endEntryAngle = AngleUtilities.QuantizeToCardinal(endEntryAngle);

        double x = startX;
        double y = startY;

        var corners = ExtractCorners(gridPath);
        int lastTurnIndex = FindLastTurningCorner(corners, currentAngle);

        // Process Manhattan routing through corners
        for (int i = 1; i < corners.Count; i++)
        {
            var (cornerX, cornerY) = _grid.GridToPhysical(corners[i].X, corners[i].Y);
            var newDirection = corners[i].Direction;
            double newAngle = AngleUtilities.DirectionToAngle(newDirection);
            bool willTurn = !AngleUtilities.IsAngleClose(currentAngle, newAngle);

            if (i == lastTurnIndex)
            {
                if (newAngle == 0 || newAngle == 180)
                    cornerY = endY;
                else if (newAngle == 90 || newAngle == 270)
                    cornerX = endX;
            }

            double dx = cornerX - x;
            double dy = cornerY - y;
            bool isHorizontal = (currentAngle == 0 || currentAngle == 180);
            bool isVertical = (currentAngle == 90 || currentAngle == 270);
            double projectedDistance = isHorizontal ? Math.Abs(dx) : Math.Abs(dy);

            double angleRad = currentAngle * Math.PI / 180;
            double dot = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
            if (dot < -0.01 && !willTurn)
                continue;

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

        // GEOMETRIC TERMINAL APPROACH - STRICT VALIDATION
        bool terminalSuccess = AppendTerminalApproach(
            routedPath, ref x, ref y, ref currentAngle, endX, endY, endEntryAngle);

        if (!terminalSuccess)
        {
            // Terminal approach failed - mark route as invalid
            routedPath.IsInvalidGeometry = true;
        }

        return routedPath;
    }

    /// <summary>
    /// Geometrically connects to end pin.
    /// INVARIANT: Final segment MUST be aligned with endEntryAngle.
    /// Returns false if valid geometry cannot be constructed.
    /// </summary>
    private bool AppendTerminalApproach(
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

        if (distance < 0.1)
            return true; // Already at pin

        // Calculate position on entry axis (where final straight should start)
        double entryAxisX, entryAxisY;
        if (endEntryAngle == 0 || endEntryAngle == 180)
        {
            // Horizontal entry - must be on line y = endY
            entryAxisX = x;
            entryAxisY = endY;
        }
        else
        {
            // Vertical entry - must be on line x = endX
            entryAxisX = endX;
            entryAxisY = y;
        }

        double lateralOffset = CalculateLateralOffset(x, y, currentAngle, endX, endY);
        double forwardDistance = CalculateForwardDistance(x, y, currentAngle, endX, endY);

        // STRATEGY 1: Already on entry axis with correct angle
        if (Math.Abs(lateralOffset) < 0.5 && AngleUtilities.IsAngleClose(currentAngle, endEntryAngle))
        {
            // Validate final segment alignment
            if (!IsLineAlignedWithAngle(x, y, endX, endY, endEntryAngle))
                return false; // Geometry violation

            if (forwardDistance > 0.5)
            {
                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }
            return true;
        }

        // STRATEGY 2: Reach entry axis, then straight to pin
        if (TryReachEntryAxis(path, ref x, ref y, ref currentAngle,
            entryAxisX, entryAxisY, endX, endY, endEntryAngle, lateralOffset, forwardDistance))
        {
            // Now on entry axis with correct angle - add final straight
            if (Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2)) > 0.5)
            {
                // CRITICAL VALIDATION
                if (!IsLineAlignedWithAngle(x, y, endX, endY, endEntryAngle))
                    return false; // Not actually on entry axis!

                path.Segments.Add(new StraightSegment(x, y, endX, endY, endEntryAngle));
                x = endX;
                y = endY;
            }
            return true;
        }

        // STRATEGY 3: S-bend if very tight
        if (distance < _minBendRadius * 4)
        {
            bool sBendSuccess = _sBendBuilder.TryBuildApproachSBend(
                path, ref x, ref y, ref currentAngle, endX, endY, endEntryAngle);

            if (sBendSuccess)
            {
                // Validate S-bend reached pin correctly
                double finalDist = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
                if (finalDist < 0.5 && AngleUtilities.IsAngleClose(currentAngle, endEntryAngle))
                    return true;
            }
        }

        // NO LAST RESORT - If we cannot solve geometry correctly, fail
        return false;
    }

    /// <summary>
    /// Attempts to reach the pin's entry axis with correct heading.
    /// INVARIANT: Upon success, current position is on entry axis and currentAngle == endEntryAngle.
    /// </summary>
    private bool TryReachEntryAxis(
        RoutedPath path,
        ref double x,
        ref double y,
        ref double currentAngle,
        double entryAxisX,
        double entryAxisY,
        double endX,
        double endY,
        double endEntryAngle,
        double lateralOffset,
        double forwardDistance)
    {
        // Need sufficient forward distance for bends
        if (forwardDistance < _minBendRadius * 1.5)
            return false;

        // Calculate how to reach entry axis
        if (Math.Abs(lateralOffset) > 0.5)
        {
            // Two-bend approach: steer toward entry axis, then align
            double bendAngle = Math.Atan(Math.Abs(lateralOffset) / (forwardDistance * 0.5)) * 180 / Math.PI;
            bendAngle = Math.Clamp(bendAngle, 5, 45);
            double bendSign = Math.Sign(lateralOffset);

            // First bend toward entry axis
            double firstTarget = currentAngle + bendAngle * bendSign;
            var bend1 = _bendBuilder.BuildBend(x, y, currentAngle, firstTarget, BendMode.Flexible);
            if (bend1 == null)
                return false;

            path.Segments.Add(bend1);
            x = bend1.EndPoint.X;
            y = bend1.EndPoint.Y;
            currentAngle = firstTarget;

            // Straight section
            double straightDist = forwardDistance * 0.4;
            if (straightDist > 0.5)
            {
                double angleRad = currentAngle * Math.PI / 180;
                double straightEndX = x + straightDist * Math.Cos(angleRad);
                double straightEndY = y + straightDist * Math.Sin(angleRad);

                // VALIDATE this straight segment
                if (!IsLineAlignedWithAngle(x, y, straightEndX, straightEndY, currentAngle))
                    return false;

                path.Segments.Add(new StraightSegment(x, y, straightEndX, straightEndY, currentAngle));
                x = straightEndX;
                y = straightEndY;
            }

            // Second bend to entry angle
            var bend2 = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible);
            if (bend2 == null)
                return false;

            path.Segments.Add(bend2);
            x = bend2.EndPoint.X;
            y = bend2.EndPoint.Y;
            currentAngle = endEntryAngle;

            // Straight to entry axis
            double toAxisX, toAxisY;
            if (endEntryAngle == 0 || endEntryAngle == 180)
            {
                toAxisX = x;
                toAxisY = endY;
            }
            else
            {
                toAxisX = endX;
                toAxisY = y;
            }

            double distToAxis = Math.Sqrt(Math.Pow(toAxisX - x, 2) + Math.Pow(toAxisY - y, 2));
            if (distToAxis > 0.5)
            {
                // VALIDATE alignment
                if (!IsLineAlignedWithAngle(x, y, toAxisX, toAxisY, currentAngle))
                    return false;

                path.Segments.Add(new StraightSegment(x, y, toAxisX, toAxisY, currentAngle));
                x = toAxisX;
                y = toAxisY;
            }

            return true;
        }
        else
        {
            // Single bend to align with entry angle
            double angleDiff = AngleUtilities.NormalizeAngle(endEntryAngle - currentAngle);
            if (Math.Abs(angleDiff) > 5)
            {
                double straightBeforeBend = forwardDistance - _minBendRadius;
                if (straightBeforeBend > 0.5)
                {
                    double angleRad = currentAngle * Math.PI / 180;
                    double straightEndX = x + straightBeforeBend * Math.Cos(angleRad);
                    double straightEndY = y + straightBeforeBend * Math.Sin(angleRad);

                    if (!IsLineAlignedWithAngle(x, y, straightEndX, straightEndY, currentAngle))
                        return false;

                    path.Segments.Add(new StraightSegment(x, y, straightEndX, straightEndY, currentAngle));
                    x = straightEndX;
                    y = straightEndY;
                }

                var bend = _bendBuilder.BuildBend(x, y, currentAngle, endEntryAngle, BendMode.Flexible);
                if (bend == null)
                    return false;

                path.Segments.Add(bend);
                x = bend.EndPoint.X;
                y = bend.EndPoint.Y;
                currentAngle = endEntryAngle;
            }

            return true;
        }
    }

    /// <summary>
    /// CRITICAL VALIDATION: Verifies that a line from (x1,y1) to (x2,y2) is geometrically
    /// aligned with the declared angle. Rejects any segment where the actual line direction
    /// does not match the declared propagation direction.
    /// </summary>
    private static bool IsLineAlignedWithAngle(
        double x1, double y1,
        double x2, double y2,
        double declaredAngle,
        double toleranceDeg = 2.0)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 0.01)
            return true; // Zero-length segment is trivially aligned

        double actualAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double angleDiff = Math.Abs(AngleUtilities.NormalizeAngle(actualAngle - declaredAngle));

        return angleDiff < toleranceDeg;
    }

    private static double CalculateLateralOffset(double x, double y, double angle, double endX, double endY)
    {
        double dx = endX - x;
        double dy = endY - y;
        double angleRad = angle * Math.PI / 180;
        return -dx * Math.Sin(angleRad) + dy * Math.Cos(angleRad);
    }

    private static double CalculateForwardDistance(double x, double y, double angle, double endX, double endY)
    {
        double dx = endX - x;
        double dy = endY - y;
        double angleRad = angle * Math.PI / 180;
        return dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
    }

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
