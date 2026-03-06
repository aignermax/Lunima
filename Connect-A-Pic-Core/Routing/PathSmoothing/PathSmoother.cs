using CAP_Core.Routing.PathSmoothing;
using CAP_Core.Routing.SegmentBuilders;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.Grid;
using CAP_Core.Routing.Utilities;
using CAP_Core.Components;
using CAP_Core.Routing;

namespace CAP_Core.Routing.PathSmoothing;

/// <summary>
/// Converts A* grid paths to physical waveguide segments.
/// Orchestrates WaypointExtractor and TerminalConnector.
/// STRICT INVARIANT: All segments must have valid geometry.
/// </summary>
public class PathSmoother
{
    private readonly PathfindingGrid _grid;
    private readonly double _minBendRadius;
    private readonly BendBuilder _bendBuilder;
    private readonly WaypointExtractor _waypointExtractor;
    private readonly TerminalConnector _terminalConnector;

    public PathSmoother(PathfindingGrid grid, double minBendRadius, List<double>? allowedRadii = null)
    {
        _grid = grid;
        _minBendRadius = minBendRadius;
        _bendBuilder = new BendBuilder(minBendRadius, allowedRadii);
        var sBendBuilder = new SBendBuilder(_bendBuilder, minBendRadius);

        _waypointExtractor = new WaypointExtractor();
        _terminalConnector = new TerminalConnector(minBendRadius, _bendBuilder, sBendBuilder);
    }

    public RoutedPath ConvertToSegments(List<AStarNode> gridPath, PhysicalPin startPin, PhysicalPin endPin)
    {
        Console.WriteLine($"[PathSmoother] Converting grid path with {gridPath?.Count ?? 0} nodes");
        var routedPath = new RoutedPath();

        if (gridPath == null || gridPath.Count < 2)
        {
            Console.WriteLine($"[PathSmoother] Grid path invalid or too short");
            return routedPath;
        }

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double currentAngle = AngleUtilities.QuantizeToCardinal(startPin.GetAbsoluteAngle());
        double endEntryAngle = AngleUtilities.NormalizeAngle(endPin.GetAbsoluteAngle() + 180);
        endEntryAngle = AngleUtilities.QuantizeToCardinal(endEntryAngle);

        double x = startX;
        double y = startY;

        // Extract waypoints from grid path
        var corners = _waypointExtractor.ExtractCorners(gridPath);
        int lastTurnIndex = _waypointExtractor.FindLastTurningCorner(corners, currentAngle);

        // Process Manhattan routing through corners UP TO last turn
        // Terminal connector will handle geometry from last turn to end pin
        ProcessManhattanSegments(
            routedPath, corners, lastTurnIndex,
            ref x, ref y, ref currentAngle,
            endX, endY);

        Console.WriteLine($"[PathSmoother] After Manhattan processing: at ({x:F2}, {y:F2}) @ {currentAngle:F1}°, need to reach ({endX:F2}, {endY:F2})");

        // Geometric terminal approach with strict validation
        bool terminalSuccess = _terminalConnector.AppendTerminalApproach(
            routedPath, ref x, ref y, ref currentAngle,
            endX, endY, endEntryAngle);

        if (!terminalSuccess)
        {
            Console.WriteLine($"[PathSmoother] Terminal approach failed - marking as invalid geometry");
            routedPath.IsInvalidGeometry = true;
        }
        else
        {
            Console.WriteLine($"[PathSmoother] SUCCESS! Generated {routedPath.Segments.Count} segments");
        }

        return routedPath;
    }

    /// <summary>
    /// Processes Manhattan routing segments through extracted corners.
    /// Inserts straight segments and 90-degree bends.
    /// Stops at lastTurnIndex to let TerminalConnector handle final approach.
    /// </summary>
    private void ProcessManhattanSegments(
        RoutedPath routedPath,
        List<(int X, int Y, GridDirection Direction)> corners,
        int lastTurnIndex,
        ref double x,
        ref double y,
        ref double currentAngle,
        double endX,
        double endY)
    {
        // Process all corners in Manhattan routing
        // TerminalConnector will handle any remaining distance + angle correction
        for (int i = 1; i < corners.Count; i++)
        {
            var (cornerX, cornerY) = _grid.GridToPhysical(corners[i].X, corners[i].Y);
            var newDirection = corners[i].Direction;
            double newAngle = AngleUtilities.DirectionToAngle(newDirection);
            bool willTurn = !AngleUtilities.IsAngleClose(currentAngle, newAngle);

            Console.WriteLine($"[PathSmoother] Processing corner {i}: grid({corners[i].X},{corners[i].Y}) phys({cornerX:F1},{cornerY:F1}) dir={newDirection} angle={newAngle}°");

            double dx = cornerX - x;
            double dy = cornerY - y;
            bool isHorizontal = (currentAngle == 0 || currentAngle == 180);
            bool isVertical = (currentAngle == 90 || currentAngle == 270);
            double projectedDistance = isHorizontal ? Math.Abs(dx) : Math.Abs(dy);

            // Check if moving forward
            double angleRad = currentAngle * Math.PI / 180;
            double dot = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
            if (dot < -0.01 && !willTurn)
            {
                Console.WriteLine($"[PathSmoother]   Skipping - backward movement (dot={dot:F2})");
                continue; // Skip backward movement
            }

            // Check if processing this corner would overshoot the end pin
            double distanceFromCornerToEnd = Math.Sqrt(
                Math.Pow(endX - cornerX, 2) + Math.Pow(endY - cornerY, 2));
            double distanceFromCurrentToEnd = Math.Sqrt(
                Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));

            // If corner is further from end than current position, skip it
            if (distanceFromCornerToEnd > distanceFromCurrentToEnd && !willTurn)
            {
                Console.WriteLine($"[PathSmoother]   Skipping - would overshoot (corner-to-end={distanceFromCornerToEnd:F1} vs current-to-end={distanceFromCurrentToEnd:F1})");
                continue;
            }

            // Calculate straight segment length (reserve space for bend if turning)
            double straightDistance = willTurn
                ? Math.Max(0, projectedDistance - _minBendRadius)
                : projectedDistance;

            double minSegmentLength = Math.Max(0.5, _grid.CellSizeMicrometers * 0.5);
            if (straightDistance > minSegmentLength && dot > 0.01)
            {
                double dirSign = isHorizontal ? (dx > 0 ? 1 : -1) : (dy > 0 ? 1 : -1);
                double endStraightX = isHorizontal ? x + dirSign * straightDistance : x;
                double endStraightY = isVertical ? y + dirSign * straightDistance : y;

                Console.WriteLine($"[PathSmoother]   Adding straight: ({x:F1},{y:F1})→({endStraightX:F1},{endStraightY:F1}) len={straightDistance:F1}");
                routedPath.Segments.Add(new StraightSegment(x, y, endStraightX, endStraightY, currentAngle));
                x = endStraightX;
                y = endStraightY;
            }

            // Insert 90-degree bend if direction changes
            if (willTurn)
            {
                Console.WriteLine($"[PathSmoother]   Considering bend: {currentAngle}° → {newAngle}°");
                var bend = _bendBuilder.BuildBend(x, y, currentAngle, newAngle, BendMode.Cardinal90);
                if (bend != null)
                {
                    // Check if bend endpoint would overshoot the end pin
                    double distBeforeBend = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
                    double distAfterBend = Math.Sqrt(Math.Pow(endX - bend.EndPoint.X, 2) + Math.Pow(endY - bend.EndPoint.Y, 2));

                    if (distAfterBend > distBeforeBend)
                    {
                        Console.WriteLine($"[PathSmoother]   Skipping bend - would overshoot (dist before={distBeforeBend:F1} after={distAfterBend:F1})");
                        break; // Stop processing corners, let TerminalConnector handle the rest
                    }

                    routedPath.Segments.Add(bend);
                    Console.WriteLine($"[PathSmoother]   Bend endpoint: ({bend.EndPoint.X:F1},{bend.EndPoint.Y:F1})");
                    x = bend.EndPoint.X;
                    y = bend.EndPoint.Y;
                    currentAngle = newAngle;
                }
                else
                {
                    currentAngle = newAngle;
                }
            }
        }
    }
}
