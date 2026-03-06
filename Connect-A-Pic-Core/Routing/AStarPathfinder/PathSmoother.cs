using CAP_Core.Components;
using CAP_Core.Routing;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Converts A* grid paths to simple waveguide segments with 90° bends at corners.
/// Simplified approach: just follow the A* path directly with rounded corners.
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
    /// Converts an A* grid path to simple routed segments.
    /// Straight lines with 90° bends at corners.
    /// </summary>
    public RoutedPath ConvertToSegments(List<AStarNode> gridPath, PhysicalPin startPin, PhysicalPin endPin)
    {
        var routedPath = new RoutedPath();

        if (gridPath == null || gridPath.Count < 2)
        {
            return routedPath;
        }

        // Get pin positions and directions
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double currentAngle = AngleUtilities.QuantizeToCardinal(startPin.GetAbsoluteAngle());
        // End pin entry angle: opposite of pin's outward facing direction
        double endEntryAngle = AngleUtilities.NormalizeAngle(endPin.GetAbsoluteAngle() + 180);
        endEntryAngle = AngleUtilities.QuantizeToCardinal(endEntryAngle);

        // Current position
        double x = startX;
        double y = startY;

        // Extract corners where direction changes
        var corners = ExtractCorners(gridPath);

        // Find the last corner that involves an actual direction change (turn).
        // This is where we snap to the pin's axis to eliminate grid quantization error.
        int lastTurnIndex = FindLastTurningCorner(corners, currentAngle);

        // Skip first corner (it's the start position) and process the rest
        for (int i = 1; i < corners.Count; i++)
        {
            var (cornerX, cornerY) = _grid.GridToPhysical(corners[i].X, corners[i].Y);
            var newDirection = corners[i].Direction; // Direction AFTER this corner
            double newAngle = AngleUtilities.DirectionToAngle(newDirection);

            // Check if direction will change at this corner
            bool willTurn = !AngleUtilities.IsAngleClose(currentAngle, newAngle);

            // For the last turning corner, snap perpendicular coordinate to the
            // end pin's exact position. This makes the bend exit precisely on the
            // pin's axis, eliminating grid quantization error without correction segments.
            if (i == lastTurnIndex)
            {
                if (newAngle == 0 || newAngle == 180) // Horizontal after turn → snap Y
                    cornerY = endY;
                else if (newAngle == 90 || newAngle == 270) // Vertical after turn → snap X
                    cornerX = endX;
            }

            // Calculate distance to corner projected onto current travel direction.
            // Use cardinal projection instead of Euclidean to avoid perpendicular
            // grid offset contaminating the distance calculation.
            double dx = cornerX - x;
            double dy = cornerY - y;
            bool isHorizontal = (currentAngle == 0 || currentAngle == 180);
            bool isVertical = (currentAngle == 90 || currentAngle == 270);
            double projectedDistance = isHorizontal ? Math.Abs(dx) : Math.Abs(dy);
            double distanceToCorner = Math.Sqrt(dx * dx + dy * dy);

            // Skip if corner is behind us (bend overshot the grid corner position).
            double angleRad = currentAngle * Math.PI / 180;
            double dot = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
            if (dot < -0.01 && !willTurn)
            {
                // Corner is behind us in our travel direction — skip straight segment
                continue;
            }

            // Check if there's enough room for proper Manhattan routing.
            // If components are too close, use smooth S-bend fallback instead.
            double effectiveBendRadius = _minBendRadius;
            bool useSBendFallback = false;

            // Detect tight space: not enough room for two 90° bends
            if (willTurn && projectedDistance < _minBendRadius * 2)
            {
                // Check if we're near the end and should use S-bend to reach pin smoothly
                double distanceToEnd = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));

                if (distanceToEnd < _minBendRadius * 4 && i >= corners.Count - 2)
                {
                    // Too close for Manhattan routing - use smooth S-bend to end pin
                    useSBendFallback = true;
                }
                else if (projectedDistance < _minBendRadius && i == lastTurnIndex)
                {
                    // Last turn is tight - try smaller radius
                    effectiveBendRadius = _bendBuilder.FindLargestRadiusAtMost(projectedDistance);
                    if (effectiveBendRadius < 0.5)
                    {
                        // Even smallest radius doesn't fit - use S-bend
                        useSBendFallback = true;
                    }
                }
            }

            // Calculate how far to go straight (leave room for bend if turning)
            double straightDistance = willTurn
                ? Math.Max(0, projectedDistance - effectiveBendRadius)
                : projectedDistance;

            // Skip very short segments (grid quantization artifacts).
            // Threshold scales with cell size to avoid micro-bends with coarse grids.
            double minSegmentLength = Math.Max(0.5, _grid.CellSizeMicrometers * 0.5);

            // Add straight segment toward corner (but stop before if we'll turn)
            if (straightDistance > minSegmentLength && dot > 0.01)
            {
                // Move exactly along the cardinal direction — no diagonal drift.
                double dirSign = isHorizontal
                    ? (dx > 0 ? 1 : -1)
                    : (dy > 0 ? 1 : -1);
                double endStraightX = isHorizontal ? x + dirSign * straightDistance : x;
                double endStraightY = isVertical ? y + dirSign * straightDistance : y;

                routedPath.Segments.Add(new StraightSegment(x, y, endStraightX, endStraightY, currentAngle));
                x = endStraightX;
                y = endStraightY;
            }

            // Add bend at corner if direction changes
            if (willTurn)
            {
                if (useSBendFallback)
                {
                    // Use smooth S-bend to reach end pin (components too close for Manhattan)
                    bool sBendSuccess = _sBendBuilder.TryBuildApproachSBend(
                        routedPath, ref x, ref y, ref currentAngle,
                        endX, endY, endEntryAngle);

                    if (sBendSuccess)
                    {
                        // S-bend connected to end pin - we're done
                        break;
                    }
                    else
                    {
                        // S-bend failed - fall through to regular bend with small radius
                        useSBendFallback = false;
                    }
                }

                if (!useSBendFallback)
                {
                    // Standard Manhattan bend
                    double? radiusOverride = (effectiveBendRadius < _minBendRadius)
                        ? effectiveBendRadius : null;
                    var bend = _bendBuilder.BuildBend(x, y, currentAngle, newAngle,
                        BendMode.Cardinal90, radiusOverride);
                    if (bend != null)
                    {
                        routedPath.Segments.Add(bend);
                        x = bend.EndPoint.X;
                        y = bend.EndPoint.Y;
                    }
                    currentAngle = newAngle; // Update current direction
                }
            }
        }

        // Final segment(s) to reach end pin.
        // Grid quantization creates perpendicular offset between the last A* corner
        // and the exact pin position. Handle by adding axis-aligned correction segments.
        double finalDx = endX - x;
        double finalDy = endY - y;
        double finalDistance = Math.Sqrt(finalDx * finalDx + finalDy * finalDy);
        if (finalDistance > 0.01)
        {
            double perpError = (endEntryAngle == 0 || endEntryAngle == 180)
                ? Math.Abs(finalDy) : Math.Abs(finalDx);

            if (perpError > 0.5)
            {
                // Perpendicular correction: add an axis-aligned segment to reach the pin axis.
                // Then add the final segment along the entry direction.
                if (endEntryAngle == 0 || endEntryAngle == 180)
                {
                    double corrAngle = finalDy > 0 ? 90.0 : 270.0;
                    routedPath.Segments.Add(new StraightSegment(x, y, x, endY, corrAngle));
                    y = endY;
                }
                else
                {
                    double corrAngle = finalDx > 0 ? 0.0 : 180.0;
                    routedPath.Segments.Add(new StraightSegment(x, y, endX, y, corrAngle));
                    x = endX;
                }
            }

            // Final segment to pin. Compute correct direction from geometry.
            double remDx = endX - x;
            double remDy = endY - y;
            double remainDist = Math.Sqrt(remDx * remDx + remDy * remDy);
            if (remainDist > 0.01)
            {
                double actualAngle = Math.Atan2(remDy, remDx) * 180 / Math.PI;
                double segmentAngle = AngleUtilities.QuantizeToCardinal(actualAngle);
                routedPath.Segments.Add(new StraightSegment(x, y, endX, endY, segmentAngle));
            }
        }

        return routedPath;
    }

    /// <summary>
    /// Finds the index of the last corner that involves a direction change.
    /// Returns -1 if no turns exist.
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
    /// Each corner stores the NEW direction after the turn.
    /// </summary>
    private List<(int X, int Y, GridDirection Direction)> ExtractCorners(List<AStarNode> gridPath)
    {
        var corners = new List<(int, int, GridDirection)>();

        if (gridPath.Count == 0)
            return corners;

        // First node is always a corner (start position with initial direction)
        corners.Add((gridPath[0].X, gridPath[0].Y, gridPath[0].Direction));

        // Add nodes where direction changes
        for (int i = 1; i < gridPath.Count; i++)
        {
            var current = gridPath[i];
            var previous = gridPath[i - 1];

            if (current.Direction != previous.Direction)
            {
                // This is a corner - store the NEW direction
                corners.Add((current.X, current.Y, current.Direction));
            }
        }

        // Last node is always a corner (if not already added)
        var lastNode = gridPath[^1];
        if (corners.Count == 0 || corners[^1] != (lastNode.X, lastNode.Y, lastNode.Direction))
        {
            corners.Add((lastNode.X, lastNode.Y, lastNode.Direction));
        }

        return corners;
    }
}
