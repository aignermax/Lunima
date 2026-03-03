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

    public PathSmoother(PathfindingGrid grid, double minBendRadius, List<double>? allowedRadii = null)
    {
        _grid = grid;
        _minBendRadius = minBendRadius;
        _bendBuilder = new BendBuilder(minBendRadius, allowedRadii);
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

        // Current position
        double x = startX;
        double y = startY;

        // Extract corners where direction changes
        var corners = ExtractCorners(gridPath);

        // Skip first corner (it's the start position) and process the rest
        for (int i = 1; i < corners.Count; i++)
        {
            var (cornerX, cornerY) = _grid.GridToPhysical(corners[i].X, corners[i].Y);
            var newDirection = corners[i].Direction; // Direction AFTER this corner
            double newAngle = AngleUtilities.DirectionToAngle(newDirection);

            // Calculate distance to corner
            double dx = cornerX - x;
            double dy = cornerY - y;
            double distanceToCorner = Math.Sqrt(dx * dx + dy * dy);

            // Check if direction will change at this corner
            bool willTurn = !AngleUtilities.IsAngleClose(currentAngle, newAngle);

            // Calculate how far to go straight (leave room for bend if turning)
            double straightDistance = willTurn
                ? Math.Max(0, distanceToCorner - _minBendRadius)
                : distanceToCorner;

            // Add straight segment toward corner (but stop before if we'll turn)
            if (straightDistance > 0.5)
            {
                double endStraightX = x + (dx / distanceToCorner) * straightDistance;
                double endStraightY = y + (dy / distanceToCorner) * straightDistance;
                routedPath.Segments.Add(new StraightSegment(x, y, endStraightX, endStraightY, currentAngle));
                x = endStraightX;
                y = endStraightY;
            }

            // Add bend at corner if direction changes
            if (willTurn)
            {
                var bend = _bendBuilder.BuildBend(x, y, currentAngle, newAngle, BendMode.Cardinal90);
                if (bend != null)
                {
                    routedPath.Segments.Add(bend);
                    x = bend.EndPoint.X;
                    y = bend.EndPoint.Y;
                }
                currentAngle = newAngle; // Update current direction
            }
        }

        // Final straight segment to snap exactly to end pin position
        double finalDistance = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2));
        if (finalDistance > 0.01)
        {
            routedPath.Segments.Add(new StraightSegment(x, y, endX, endY, currentAngle));
        }

        return routedPath;
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
