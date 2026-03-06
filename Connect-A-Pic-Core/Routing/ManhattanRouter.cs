using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// Simple geometric Manhattan routing using only horizontal/vertical segments with 90° bends.
/// Used as a fallback when A* pathfinding fails.
/// Routes are computed in a canonical frame (start faces East) then transformed to world space.
/// </summary>
public class ManhattanRouter
{
    private readonly double _bendRadius;

    public ManhattanRouter(double minBendRadius)
    {
        _bendRadius = minBendRadius;
    }

    public void Route(double startX, double startY, double startAngle,
                      double endX, double endY, double endInputAngle,
                      RoutedPath path)
    {
        int startDir = (int)AngleUtilities.QuantizeToCardinal(startAngle);
        int endDir = (int)AngleUtilities.QuantizeToCardinal(endInputAngle);
        double dx = endX - startX;
        double dy = endY - startY;

        var (fwd, lat) = ToCanonical(dx, dy, startDir);
        int relEndDir = ((endDir - startDir) % 360 + 360) % 360;

        var canonPath = new RoutedPath();
        RouteCanonical(canonPath, fwd, lat, relEndDir);
        TransformToWorld(canonPath, path, startX, startY, startDir);
        FixEndpoints(path, startX, startY, startDir, endX, endY);
    }

    private void RouteCanonical(RoutedPath path, double fwd, double lat, int endDir)
    {
        double r = _bendRadius;
        switch (endDir)
        {
            case 0:   RouteSameDirection(path, fwd, lat, r); break;
            case 180: RouteOppositeDirection(path, fwd, lat, r); break;
            case 90:
            case 270: RoutePerpendicular(path, fwd, lat, r, endDir); break;
        }
    }

    /// <summary>
    /// Both pins face the same direction (East in canonical).
    /// </summary>
    private static void RouteSameDirection(RoutedPath path, double fwd, double lat, double r)
    {
        if (Math.Abs(lat) < 0.5)
        {
            if (fwd > 0.5)
                path.Segments.Add(new StraightSegment(0, 0, fwd, 0, 0));
            return;
        }

        int turnDir = lat > 0 ? 90 : 270;

        if (fwd > 2 * r)
        {
            double mid = fwd / 2;
            Straight(path, 0, 0, mid - r, 0, 0);
            AddBend(path, mid - r, 0, 0, turnDir, r);
            var p = Last(path);
            double vertTarget = lat + (lat > 0 ? -r : r);
            Straight(path, p.X, p.Y, p.X, vertTarget, turnDir);
            p = Last(path);
            AddBend(path, p.X, p.Y, turnDir, 0, r);
            p = Last(path);
            Straight(path, p.X, p.Y, fwd, lat, 0);
        }
        else
        {
            // Limited forward room: escape forward by one bend radius
            Straight(path, 0, 0, r, 0, 0);
            AddBend(path, r, 0, 0, turnDir, r);
            var p = Last(path);
            double vertDist = Math.Abs(lat) - 2 * r;
            if (vertDist > 0.5)
            {
                double newY = p.Y + (lat > 0 ? vertDist : -vertDist);
                path.Segments.Add(new StraightSegment(p.X, p.Y, p.X, newY, turnDir));
            }
            p = Last(path);
            AddBend(path, p.X, p.Y, turnDir, 0, r);
        }
    }

    /// <summary>
    /// Pins face opposite directions (East vs West in canonical).
    /// </summary>
    private static void RouteOppositeDirection(RoutedPath path, double fwd, double lat, double r)
    {
        int turnSide;
        if (Math.Abs(lat) >= 2 * r)
            turnSide = lat >= 0 ? 1 : -1;
        else
            turnSide = lat > 0 ? -1 : 1; // U-turn through opposite side

        int turnDir = turnSide > 0 ? 90 : 270;

        if (Math.Abs(lat) >= 2 * r)
        {
            // Enough lateral room: simple 2-bend route
            double fwdTarget = Math.Max(fwd, r);
            Straight(path, 0, 0, fwdTarget, 0, 0);
            AddBend(path, fwdTarget, 0, 0, turnDir, r);
            var p = Last(path);
            double latTarget = lat - turnSide * r;
            Straight(path, p.X, p.Y, p.X, latTarget, turnDir);
            p = Last(path);
            AddBend(path, p.X, p.Y, turnDir, 180, r);
            p = Last(path);
            Straight(path, p.X, p.Y, fwd, lat, 180);
        }
        else
        {
            // Small lateral offset: 4-bend U-turn
            double fwdPast = Math.Max(fwd + 2 * r, 2 * r);
            Straight(path, 0, 0, fwdPast, 0, 0);
            AddBend(path, fwdPast, 0, 0, turnDir, r);
            var p = Last(path);
            double peakLat = turnSide * 2 * r;
            double latBend = peakLat - turnSide * r;
            Straight(path, p.X, p.Y, p.X, latBend, turnDir);
            p = Last(path);
            AddBend(path, p.X, p.Y, turnDir, 180, r);
            p = Last(path);

            double adjustX = fwd + 2 * r;
            Straight(path, p.X, p.Y, adjustX, p.Y, 180);
            p = Last(path);

            int turn2 = lat > p.Y ? 90 : 270;
            AddBend(path, p.X, p.Y, 180, turn2, r);
            p = Last(path);
            double yFinal = lat + (turn2 == 90 ? -r : r);
            Straight(path, p.X, p.Y, p.X, yFinal, turn2);
            p = Last(path);
            AddBend(path, p.X, p.Y, turn2, 180, r);
        }
    }

    /// <summary>
    /// End pin faces perpendicular (North=90 or South=270 in canonical).
    /// </summary>
    private static void RoutePerpendicular(
        RoutedPath path, double fwd, double lat, double r, int endDir)
    {
        int latSign = endDir == 90 ? 1 : -1;

        if (fwd > r && lat * latSign > r)
        {
            // Favorable L-bend: end is forward and in the natural direction
            Straight(path, 0, 0, fwd - r, 0, 0);
            AddBend(path, fwd - r, 0, 0, endDir, r);
            var p = Last(path);
            Straight(path, p.X, p.Y, fwd, lat, endDir);
            return;
        }

        if (fwd > r && lat * latSign < -r)
        {
            // Unfavorable: end is forward but in wrong lateral direction
            int wrongTurn = endDir == 90 ? 270 : 90;
            Straight(path, 0, 0, fwd - r, 0, 0);
            AddBend(path, fwd - r, 0, 0, wrongTurn, r);
            var p = Last(path);
            double vertTarget = lat - latSign * r;
            Straight(path, p.X, p.Y, p.X, vertTarget, wrongTurn);
            p = Last(path);
            AddBend(path, p.X, p.Y, wrongTurn, 0, r);
            p = Last(path);
            AddBend(path, p.X, p.Y, 0, endDir, r);
            return;
        }

        // Detour: target is behind or too close
        RoutePerpendicularDetour(path, fwd, lat, r, endDir);
    }

    private static void RoutePerpendicularDetour(
        RoutedPath path, double fwd, double lat, double r, int endDir)
    {
        path.Segments.Add(new StraightSegment(0, 0, r, 0, 0));
        int firstTurn = lat >= 0 ? 90 : 270;
        AddBend(path, r, 0, 0, firstTurn, r);
        var p = Last(path);

        double targetY = endDir == 90
            ? (lat >= 0 ? Math.Max(lat + r, p.Y + r) : lat - r)
            : (lat >= 0 ? lat + r : Math.Min(lat - r, p.Y - r));
        Straight(path, p.X, p.Y, p.X, targetY, firstTurn);
        p = Last(path);

        int hDir = fwd > p.X ? 0 : 180;
        AddBend(path, p.X, p.Y, firstTurn, hDir, r);
        p = Last(path);

        double hTarget = fwd - (hDir == 0 ? r : -r);
        if (Math.Abs(hTarget - p.X) > r + 0.5)
        {
            Straight(path, p.X, p.Y, hTarget, p.Y, hDir);
            p = Last(path);
        }

        AddBend(path, p.X, p.Y, hDir, endDir, r);
        p = Last(path);
        Straight(path, p.X, p.Y, fwd, lat, endDir);
    }

    // --- Helpers ---

    private static (double X, double Y) Last(RoutedPath path) => path.Segments[^1].EndPoint;

    private static void Straight(RoutedPath path,
        double x1, double y1, double x2, double y2, double angle)
    {
        double dist = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        if (dist > 0.5)
            path.Segments.Add(new StraightSegment(x1, y1, x2, y2, angle));
    }

    private static void AddBend(RoutedPath path, double x, double y,
                                double startAngle, double endAngle, double radius)
    {
        double sweepAngle = AngleUtilities.NormalizeAngle(endAngle - startAngle);
        if (Math.Abs(sweepAngle) > 90)
            sweepAngle = Math.Sign(sweepAngle) * 90;

        double bendDirection = Math.Sign(sweepAngle);
        if (bendDirection == 0) bendDirection = 1;

        double startRad = startAngle * Math.PI / 180;
        double perpX = -Math.Sin(startRad) * bendDirection;
        double perpY = Math.Cos(startRad) * bendDirection;

        double centerX = x + perpX * radius;
        double centerY = y + perpY * radius;

        path.Segments.Add(new BendSegment(centerX, centerY, radius, startAngle, sweepAngle));
    }

    // --- Coordinate transforms ---

    private static (double fwd, double lat) ToCanonical(double dx, double dy, int startDir)
    {
        return startDir switch
        {
            0   => (dx, dy),
            90  => (dy, -dx),
            180 => (-dx, -dy),
            270 => (-dy, dx),
            _   => (dx, dy)
        };
    }

    private static (double x, double y) ToWorld(double fwd, double lat, int startDir)
    {
        return startDir switch
        {
            0   => (fwd, lat),
            90  => (-lat, fwd),
            180 => (-fwd, -lat),
            270 => (lat, -fwd),
            _   => (fwd, lat)
        };
    }

    private static void TransformToWorld(RoutedPath canonPath, RoutedPath worldPath,
                                          double originX, double originY, int startDir)
    {
        foreach (var segment in canonPath.Segments)
        {
            if (segment is StraightSegment straight)
            {
                var (sx, sy) = ToWorld(straight.StartPoint.X, straight.StartPoint.Y, startDir);
                var (ex, ey) = ToWorld(straight.EndPoint.X, straight.EndPoint.Y, startDir);
                double worldAngle = ((straight.StartAngleDegrees + startDir) % 360 + 360) % 360;
                worldPath.Segments.Add(new StraightSegment(
                    originX + sx, originY + sy,
                    originX + ex, originY + ey,
                    worldAngle));
            }
            else if (segment is BendSegment bend)
            {
                var (cx, cy) = ToWorld(bend.Center.X, bend.Center.Y, startDir);
                double worldStartAngle = ((bend.StartAngleDegrees + startDir) % 360 + 360) % 360;
                worldPath.Segments.Add(new BendSegment(
                    originX + cx, originY + cy,
                    bend.RadiusMicrometers,
                    worldStartAngle,
                    bend.SweepAngleDegrees));
            }
        }
    }

    private static void FixEndpoints(RoutedPath path, double startX, double startY,
                                     int startDir, double endX, double endY)
    {
        if (path.Segments.Count == 0) return;

        var firstSeg = path.Segments[0];
        double startDist = Distance(firstSeg.StartPoint.X, firstSeg.StartPoint.Y, startX, startY);
        if (startDist > 0.01)
        {
            if (firstSeg is StraightSegment straight)
            {
                path.Segments[0] = new StraightSegment(
                    startX, startY, straight.EndPoint.X, straight.EndPoint.Y,
                    straight.StartAngleDegrees);
            }
            else
            {
                path.Segments.Insert(0, new StraightSegment(
                    startX, startY, firstSeg.StartPoint.X, firstSeg.StartPoint.Y, startDir));
            }
        }

        var lastSeg = path.Segments[^1];
        double endDist = Distance(lastSeg.EndPoint.X, lastSeg.EndPoint.Y, endX, endY);
        if (endDist > 0.01)
        {
            if (lastSeg is StraightSegment straight)
            {
                var projected = ProjectOntoDirection(
                    straight.StartPoint, straight.StartAngleDegrees, endX, endY);
                path.Segments[^1] = new StraightSegment(
                    straight.StartPoint.X, straight.StartPoint.Y,
                    projected.X, projected.Y, straight.StartAngleDegrees);
            }
            else if (lastSeg is BendSegment bend)
            {
                var projected = ProjectOntoDirection(
                    bend.EndPoint, bend.EndAngleDegrees, endX, endY);
                double dist = Distance(bend.EndPoint.X, bend.EndPoint.Y, projected.X, projected.Y);
                if (dist > 0.01)
                {
                    path.Segments.Add(new StraightSegment(
                        bend.EndPoint.X, bend.EndPoint.Y,
                        projected.X, projected.Y, bend.EndAngleDegrees));
                }
            }
        }
    }

    private static (double X, double Y) ProjectOntoDirection(
        (double X, double Y) origin, double angleDeg, double targetX, double targetY)
    {
        double rad = angleDeg * Math.PI / 180;
        double fwdX = Math.Cos(rad);
        double fwdY = Math.Sin(rad);
        double dx = targetX - origin.X;
        double dy = targetY - origin.Y;
        double fwdDist = dx * fwdX + dy * fwdY;
        return (origin.X + fwdX * fwdDist, origin.Y + fwdY * fwdDist);
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    }
}
