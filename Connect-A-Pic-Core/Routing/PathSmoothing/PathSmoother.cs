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

        // Process Manhattan routing through corners
        ProcessManhattanSegments(
            routedPath, corners, lastTurnIndex,
            ref x, ref y, ref currentAngle,
            endX, endY);

        Console.WriteLine($"[PathSmoother] After Manhattan processing: at ({x:F1},{y:F1}) @ {currentAngle}°, need to reach ({endX:F1},{endY:F1})");

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
        for (int i = 1; i < corners.Count; i++)
        {
            var (cornerX, cornerY) = _grid.GridToPhysical(corners[i].X, corners[i].Y);
            var newDirection = corners[i].Direction;
            double newAngle = AngleUtilities.DirectionToAngle(newDirection);
            bool willTurn = !AngleUtilities.IsAngleClose(currentAngle, newAngle);

            // Snap last turning corner to end pin axis
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

            // Check if moving forward
            double angleRad = currentAngle * Math.PI / 180;
            double dot = dx * Math.Cos(angleRad) + dy * Math.Sin(angleRad);
            if (dot < -0.01 && !willTurn)
                continue; // Skip backward movement

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

                routedPath.Segments.Add(new StraightSegment(x, y, endStraightX, endStraightY, currentAngle));
                x = endStraightX;
                y = endStraightY;
            }

            // Insert 90-degree bend if direction changes
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
    }
}
