using CAP_Core.Components;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Converts A* grid paths to smooth waveguide path segments.
/// The A* already produces Manhattan paths - this class just adds proper bends at corners.
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
        _allowedRadii = allowedRadii?.OrderBy(r => r).ToList() ?? new List<double>();
    }

    /// <summary>
    /// Converts a grid path to waveguide segments.
    /// </summary>
    public RoutedPath ConvertToSegments(List<AStarNode> gridPath,
                                         PhysicalPin startPin, PhysicalPin endPin)
    {
        var routedPath = new RoutedPath();

        if (gridPath == null || gridPath.Count < 2)
        {
            return routedPath;
        }

        // Get pin positions and directions
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = QuantizeToCardinal(startPin.GetAbsoluteAngle());
        double endInputAngle = QuantizeToCardinal(NormalizeAngle(endPin.GetAbsoluteAngle() + 180));

        // Extract corner points where direction changes (keeps direction info)
        var corners = ExtractCornerPoints(gridPath);

        // Build the path following the A* directions
        double x = startX;
        double y = startY;
        double angle = startAngle;

        for (int i = 0; i < corners.Count; i++)
        {
            var corner = corners[i];
            var (targetX, targetY) = _grid.GridToPhysical(corner.X, corner.Y);

            // Determine required direction from A* path
            GridDirection requiredDir;
            if (i < corners.Count - 1)
            {
                // Use the direction of the NEXT corner (that's the direction we travel to get there)
                requiredDir = corners[i + 1].Direction;
            }
            else
            {
                // Last corner - use the direction we need to reach the end pin
                requiredDir = corner.Direction;
            }

            // Convert GridDirection to angle
            double astarAngle = DirectionToAngle(requiredDir);

            // Move to this corner point, following the A* direction
            MoveToPointFollowingDirection(routedPath, ref x, ref y, ref angle, targetX, targetY, astarAngle);
        }

        // Final connection to end pin with required end angle
        MoveToPointFollowingDirection(routedPath, ref x, ref y, ref angle, endX, endY, endInputAngle);

        // Make sure we end at the correct angle
        if (!IsAngleMatch(angle, endInputAngle))
        {
            AddBend(routedPath, ref x, ref y, angle, endInputAngle);
            angle = endInputAngle;
        }

        // IMPORTANT: Ensure the path starts and ends EXACTLY at the pin positions.
        // This prevents the pathIsStale check in rendering from triggering a fallback line.
        if (routedPath.Segments.Count > 0)
        {
            // Fix start position
            var firstSeg = routedPath.Segments[0];
            double startDistCheck = Math.Sqrt(Math.Pow(firstSeg.StartPoint.X - startX, 2) +
                                              Math.Pow(firstSeg.StartPoint.Y - startY, 2));
            if (startDistCheck > 0.01)
            {
                if (firstSeg is StraightSegment straight)
                {
                    routedPath.Segments[0] = new StraightSegment(
                        startX, startY,
                        straight.EndPoint.X, straight.EndPoint.Y,
                        straight.StartAngleDegrees);
                }
                else if (firstSeg is BendSegment)
                {
                    routedPath.Segments.Insert(0, new StraightSegment(
                        startX, startY,
                        firstSeg.StartPoint.X, firstSeg.StartPoint.Y,
                        startAngle));
                }
            }

            // Fix end position
            var lastSeg = routedPath.Segments[^1];
            double endDistCheck = Math.Sqrt(Math.Pow(lastSeg.EndPoint.X - endX, 2) +
                                            Math.Pow(lastSeg.EndPoint.Y - endY, 2));
            if (endDistCheck > 0.01)
            {
                if (lastSeg is StraightSegment straight)
                {
                    routedPath.Segments[^1] = new StraightSegment(
                        straight.StartPoint.X, straight.StartPoint.Y,
                        endX, endY, straight.StartAngleDegrees);
                }
                else if (lastSeg is BendSegment bend)
                {
                    routedPath.Segments.Add(new StraightSegment(
                        bend.EndPoint.X, bend.EndPoint.Y,
                        endX, endY, bend.EndAngleDegrees));
                }
            }
        }

        return routedPath;
    }

    /// <summary>
    /// Converts GridDirection to angle in degrees.
    /// </summary>
    private static double DirectionToAngle(GridDirection dir)
    {
        return dir switch
        {
            GridDirection.East => 0,
            GridDirection.North => 90,
            GridDirection.West => 180,
            GridDirection.South => 270,
            _ => 0
        };
    }

    /// <summary>
    /// Moves from current position to target, using the A* suggested direction when possible.
    /// </summary>
    private void MoveToPointFollowingDirection(RoutedPath path, ref double x, ref double y, ref double angle,
                                                 double targetX, double targetY, double suggestedAngle)
    {
        double dx = targetX - x;
        double dy = targetY - y;

        // Skip if already at target
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
        {
            return;
        }

        double bendRadius = _minBendRadius;
        bool needH = Math.Abs(dx) > 0.5;
        bool needV = Math.Abs(dy) > 0.5;

        // Determine the actual directions needed
        double hAngle = dx > 0 ? 0 : 180;    // East or West
        double vAngle = dy > 0 ? 90 : 270;   // North or South

        if (needH && needV)
        {
            // Need L-shape - use A* suggested direction to decide order
            bool suggestedIsHorizontal = IsHorizontal(suggestedAngle);
            bool goHFirst = suggestedIsHorizontal;

            // But if current angle already matches one direction, prefer continuing that way
            if (IsAngleMatch(angle, hAngle))
            {
                goHFirst = true;
            }
            else if (IsAngleMatch(angle, vAngle))
            {
                goHFirst = false;
            }

            if (goHFirst)
            {
                // Turn to horizontal if needed
                if (!IsAngleMatch(angle, hAngle))
                {
                    AddBend(path, ref x, ref y, angle, hAngle);
                    angle = hAngle;
                }

                // Go horizontal (leave room for bend)
                double hDist = Math.Abs(dx) - bendRadius;
                if (hDist > 0.5)
                {
                    double hEndX = x + Math.Sign(dx) * hDist;
                    AddStraight(path, ref x, ref y, hEndX, y, angle);
                }

                // Turn to vertical
                AddBend(path, ref x, ref y, angle, vAngle);
                angle = vAngle;

                // Go vertical to target
                if (Math.Abs(targetY - y) > 0.5)
                {
                    AddStraight(path, ref x, ref y, x, targetY, angle);
                }
            }
            else
            {
                // Turn to vertical if needed
                if (!IsAngleMatch(angle, vAngle))
                {
                    AddBend(path, ref x, ref y, angle, vAngle);
                    angle = vAngle;
                }

                // Go vertical (leave room for bend)
                double vDist = Math.Abs(dy) - bendRadius;
                if (vDist > 0.5)
                {
                    double vEndY = y + Math.Sign(dy) * vDist;
                    AddStraight(path, ref x, ref y, x, vEndY, angle);
                }

                // Turn to horizontal
                AddBend(path, ref x, ref y, angle, hAngle);
                angle = hAngle;

                // Go horizontal to target
                if (Math.Abs(targetX - x) > 0.5)
                {
                    AddStraight(path, ref x, ref y, targetX, y, angle);
                }
            }
        }
        else if (needH)
        {
            // Only horizontal movement
            if (!IsAngleMatch(angle, hAngle))
            {
                AddBend(path, ref x, ref y, angle, hAngle);
                angle = hAngle;
            }
            AddStraight(path, ref x, ref y, targetX, y, angle);
        }
        else if (needV)
        {
            // Only vertical movement
            if (!IsAngleMatch(angle, vAngle))
            {
                AddBend(path, ref x, ref y, angle, vAngle);
                angle = vAngle;
            }
            AddStraight(path, ref x, ref y, x, targetY, angle);
        }
    }

    /// <summary>
    /// Adds a straight segment.
    /// </summary>
    private void AddStraight(RoutedPath path, ref double x, ref double y,
                              double endX, double endY, double angle)
    {
        path.Segments.Add(new StraightSegment(x, y, endX, endY, angle));
        x = endX;
        y = endY;
    }

    /// <summary>
    /// Adds a 90° bend.
    /// </summary>
    private void AddBend(RoutedPath path, ref double x, ref double y,
                          double fromAngle, double toAngle)
    {
        double sweepAngle = NormalizeAngle(toAngle - fromAngle);

        // Clamp to 90 degrees max
        if (Math.Abs(sweepAngle) > 90)
        {
            sweepAngle = Math.Sign(sweepAngle) * 90;
        }

        if (Math.Abs(sweepAngle) < 5)
        {
            return; // No bend needed
        }

        double radius = SelectRadius(_minBendRadius);
        double bendDir = Math.Sign(sweepAngle);
        if (bendDir == 0) bendDir = 1;

        // Calculate center: perpendicular to start direction
        double startRad = fromAngle * Math.PI / 180;
        double perpX = -Math.Sin(startRad) * bendDir;
        double perpY = Math.Cos(startRad) * bendDir;

        double centerX = x + perpX * radius;
        double centerY = y + perpY * radius;

        var bend = new BendSegment(centerX, centerY, radius, fromAngle, sweepAngle);
        path.Segments.Add(bend);

        x = bend.EndPoint.X;
        y = bend.EndPoint.Y;
    }

    /// <summary>
    /// Selects bend radius from allowed values.
    /// </summary>
    private double SelectRadius(double minRequired)
    {
        if (_allowedRadii.Count == 0)
            return Math.Max(minRequired, _minBendRadius);

        foreach (var r in _allowedRadii)
        {
            if (r >= minRequired)
                return r;
        }
        return _allowedRadii[^1];
    }

    /// <summary>
    /// Extracts corner points where direction changes.
    /// </summary>
    private List<AStarNode> ExtractCornerPoints(List<AStarNode> path)
    {
        if (path.Count <= 2)
            return new List<AStarNode>(path);

        var corners = new List<AStarNode> { path[0] };

        for (int i = 1; i < path.Count - 1; i++)
        {
            if (path[i].Direction != path[i + 1].Direction)
            {
                corners.Add(path[i]);
            }
        }

        corners.Add(path[^1]);
        return corners;
    }

    private static double QuantizeToCardinal(double angle)
    {
        angle = NormalizeAngle(angle);
        if (angle >= -45 && angle < 45) return 0;
        if (angle >= 45 && angle < 135) return 90;
        if (angle >= 135 || angle < -135) return 180;
        return 270;
    }

    private static bool IsHorizontal(double angle)
    {
        angle = NormalizeAngle(angle);
        return Math.Abs(angle) < 10 || Math.Abs(Math.Abs(angle) - 180) < 10;
    }

    private static bool IsVertical(double angle)
    {
        angle = NormalizeAngle(angle);
        return Math.Abs(Math.Abs(angle) - 90) < 10 || Math.Abs(Math.Abs(angle) - 270) < 10;
    }

    private static bool IsAngleMatch(double a1, double a2)
    {
        return Math.Abs(NormalizeAngle(a1 - a2)) < 10;
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle <= -180) angle += 360;
        return angle;
    }
}
