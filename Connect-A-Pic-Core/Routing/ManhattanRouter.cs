using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// Simple geometric Manhattan routing using only horizontal/vertical segments with 90° bends.
/// Used as a fallback when A* pathfinding fails.
/// </summary>
public class ManhattanRouter
{
    private readonly double _bendRadius;

    public ManhattanRouter(double minBendRadius)
    {
        _bendRadius = minBendRadius;
    }

    /// <summary>
    /// Routes a Manhattan path between two cardinal-direction endpoints.
    /// </summary>
    public void Route(double startX, double startY, double startAngle,
                      double endX, double endY, double endInputAngle,
                      RoutedPath path)
    {
        double r = _bendRadius;
        int startDir = (int)AngleUtilities.QuantizeToCardinal(startAngle);
        int endDir = (int)AngleUtilities.QuantizeToCardinal(endInputAngle);
        double dx = endX - startX;
        double dy = endY - startY;

        RouteExplicit(path, startX, startY, startDir, endX, endY, endDir, dx, dy, r);
        FixEndpoints(path, startX, startY, startDir, endX, endY);
    }

    /// <summary>
    /// Fixes segment start/end to match exact pin positions.
    /// </summary>
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
            else if (firstSeg is BendSegment)
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
                var projected = ProjectOntoDirection(straight.StartPoint, straight.StartAngleDegrees, endX, endY);
                path.Segments[^1] = new StraightSegment(
                    straight.StartPoint.X, straight.StartPoint.Y,
                    projected.X, projected.Y, straight.StartAngleDegrees);
            }
            else if (lastSeg is BendSegment bend)
            {
                var projected = ProjectOntoDirection(bend.EndPoint, bend.EndAngleDegrees, endX, endY);
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
        return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
    }

    private void RouteExplicit(RoutedPath path, double x, double y, int startDir,
                               double endX, double endY, int endDir,
                               double dx, double dy, double r)
    {
        switch (startDir)
        {
            case 0: RouteFromEast(path, x, y, endX, endY, endDir, dx, dy, r); break;
            case 90: RouteFromNorth(path, x, y, endX, endY, endDir, dx, dy, r); break;
            case 180: RouteFromWest(path, x, y, endX, endY, endDir, dx, dy, r); break;
            case 270: RouteFromSouth(path, x, y, endX, endY, endDir, dx, dy, r); break;
        }
    }

    private void RouteFromEast(RoutedPath path, double x, double y,
                                double endX, double endY, int endDir,
                                double dx, double dy, double r)
    {
        switch (endDir)
        {
            case 0: RouteEastToEast(path, x, y, endX, endY, dx, dy, r); break;
            case 90: RouteEastToNorth(path, x, y, endX, endY, dx, dy, r); break;
            case 180: RouteEastToWest(path, x, y, endX, endY, dx, dy, r); break;
            case 270: RouteEastToSouth(path, x, y, endX, endY, dx, dy, r); break;
        }
    }

    private void RouteEastToEast(RoutedPath path, double x, double y,
                                  double endX, double endY, double dx, double dy, double r)
    {
        if (dx > 2 * r)
        {
            double midX = x + dx / 2;
            path.Segments.Add(new StraightSegment(x, y, midX - r, y, 0));
            int turnDir = dy >= 0 ? 90 : 270;
            AddBend(path, midX - r, y, 0, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            double vertTargetY = endY + (dy >= 0 ? -r : r);
            int vertAngle = vertTargetY >= b1.Y ? 90 : 270;
            if (Math.Abs(vertTargetY - b1.Y) > 0.5)
                path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, vertTargetY, vertAngle));
            AddBend(path, b1.X, vertTargetY, turnDir, 0, r);
            var b2 = path.Segments[^1].EndPoint;
            if (Math.Abs(endX - b2.X) > 0.5)
                path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX, endY, 0));
        }
        else
        {
            double escape = Math.Max(r, Math.Abs(dx) + r);
            path.Segments.Add(new StraightSegment(x, y, x + escape, y, 0));
            int turnDir = dy >= 0 ? 90 : 270;
            AddBend(path, x + escape, y, 0, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            double vertDist = Math.Abs(dy) - 2 * r;
            if (vertDist > 0.5)
                path.Segments.Add(new StraightSegment(b1.X, b1.Y,
                    b1.X, b1.Y + (dy >= 0 ? vertDist : -vertDist), turnDir));
            var afterVert = path.Segments[^1].EndPoint;
            AddBend(path, afterVert.X, afterVert.Y, turnDir, 0, r);
        }
    }

    private void RouteEastToNorth(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dx > r && dy > r)
        {
            path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
            AddBend(path, endX - r, y, 0, 90, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 90));
        }
        else if (dx > r && dy < -r)
        {
            path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
            AddBend(path, endX - r, y, 0, 270, r);
            var b1 = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, endY - r, 270));
            AddBend(path, b1.X, endY - r, 270, 0, r);
            var b2 = path.Segments[^1].EndPoint;
            AddBend(path, b2.X, b2.Y, 0, 90, r);
        }
        else
        {
            RouteDetourToVertical(path, x, y, endX, endY, dx, dy, r, 0, 90);
        }
    }

    private void RouteEastToWest(RoutedPath path, double x, double y,
                                  double endX, double endY, double dx, double dy, double r)
    {
        RouteOpposite(path, x, y, endX, endY, dy, r, 0, 180);
    }

    private void RouteEastToSouth(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dx > r && dy < -r)
        {
            path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
            AddBend(path, endX - r, y, 0, 270, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 270));
        }
        else if (dx > r && dy > r)
        {
            path.Segments.Add(new StraightSegment(x, y, endX - r, y, 0));
            AddBend(path, endX - r, y, 0, 90, r);
            var b1 = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, endY + r, 90));
            AddBend(path, b1.X, endY + r, 90, 0, r);
            var b2 = path.Segments[^1].EndPoint;
            AddBend(path, b2.X, b2.Y, 0, 270, r);
        }
        else
        {
            RouteDetourToVertical(path, x, y, endX, endY, dx, dy, r, 0, 270);
        }
    }

    private void RouteFromNorth(RoutedPath path, double x, double y,
                                 double endX, double endY, int endDir,
                                 double dx, double dy, double r)
    {
        switch (endDir)
        {
            case 90: RouteNorthToNorth(path, x, y, endX, endY, dx, dy, r); break;
            case 0: RouteNorthToEast(path, x, y, endX, endY, dx, dy, r); break;
            case 180: RouteNorthToWest(path, x, y, endX, endY, dx, dy, r); break;
            case 270: RouteNorthToSouth(path, x, y, endX, endY, dx, dy, r); break;
        }
    }

    private void RouteNorthToNorth(RoutedPath path, double x, double y,
                                    double endX, double endY, double dx, double dy, double r)
    {
        if (dy > 2 * r)
        {
            double midY = y + dy / 2;
            path.Segments.Add(new StraightSegment(x, y, x, midY - r, 90));
            int turnDir = dx >= 0 ? 0 : 180;
            AddBend(path, x, midY - r, 90, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b1.X, b1.Y,
                endX + (dx >= 0 ? -r : r), b1.Y, turnDir));
            AddBend(path, endX + (dx >= 0 ? -r : r), b1.Y, turnDir, 90, r);
            var b2 = path.Segments[^1].EndPoint;
            if (Math.Abs(endY - b2.Y) > 0.5)
                path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX, endY, 90));
        }
        else
        {
            double escape = Math.Max(r, Math.Abs(dy) + r);
            path.Segments.Add(new StraightSegment(x, y, x, y + escape, 90));
            int turnDir = dx >= 0 ? 0 : 180;
            AddBend(path, x, y + escape, 90, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            double horizDist = Math.Abs(dx) - 2 * r;
            if (horizDist > 0.5)
                path.Segments.Add(new StraightSegment(b1.X, b1.Y,
                    b1.X + (dx >= 0 ? horizDist : -horizDist), b1.Y, turnDir));
            var afterH = path.Segments[^1].EndPoint;
            AddBend(path, afterH.X, afterH.Y, turnDir, 90, r);
        }
    }

    private void RouteNorthToEast(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dy > r && dx > r)
        {
            path.Segments.Add(new StraightSegment(x, y, x, endY - r, 90));
            AddBend(path, x, endY - r, 90, 0, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 0));
        }
        else
        {
            RouteDetourToHorizontal(path, x, y, endX, endY, dx, dy, r, 90, 0);
        }
    }

    private void RouteNorthToWest(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dy > r && dx < -r)
        {
            path.Segments.Add(new StraightSegment(x, y, x, endY - r, 90));
            AddBend(path, x, endY - r, 90, 180, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 180));
        }
        else
        {
            RouteDetourToHorizontal(path, x, y, endX, endY, dx, dy, r, 90, 180);
        }
    }

    private void RouteNorthToSouth(RoutedPath path, double x, double y,
                                    double endX, double endY, double dx, double dy, double r)
    {
        RouteOppositeVertical(path, x, y, endX, endY, dx, r, 90, 270);
    }

    private void RouteFromWest(RoutedPath path, double x, double y,
                                double endX, double endY, int endDir,
                                double dx, double dy, double r)
    {
        switch (endDir)
        {
            case 180: RouteWestToWest(path, x, y, endX, endY, dx, dy, r); break;
            case 90: RouteWestToNorth(path, x, y, endX, endY, dx, dy, r); break;
            case 0: RouteWestToEast(path, x, y, endX, endY, dx, dy, r); break;
            case 270: RouteWestToSouth(path, x, y, endX, endY, dx, dy, r); break;
        }
    }

    private void RouteWestToWest(RoutedPath path, double x, double y,
                                  double endX, double endY, double dx, double dy, double r)
    {
        if (dx < -2 * r)
        {
            double midX = x + dx / 2;
            path.Segments.Add(new StraightSegment(x, y, midX + r, y, 180));
            int turnDir = dy >= 0 ? 90 : 270;
            AddBend(path, midX + r, y, 180, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b1.X, b1.Y,
                b1.X, endY + (dy >= 0 ? -r : r), turnDir));
            AddBend(path, b1.X, endY + (dy >= 0 ? -r : r), turnDir, 180, r);
            var b2 = path.Segments[^1].EndPoint;
            if (Math.Abs(endX - b2.X) > 0.5)
                path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX, endY, 180));
        }
        else
        {
            double escape = Math.Max(r, Math.Abs(dx) + r);
            path.Segments.Add(new StraightSegment(x, y, x - escape, y, 180));
            int turnDir = dy >= 0 ? 90 : 270;
            AddBend(path, x - escape, y, 180, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            double vertDist = Math.Abs(dy) - 2 * r;
            if (vertDist > 0.5)
                path.Segments.Add(new StraightSegment(b1.X, b1.Y,
                    b1.X, b1.Y + (dy >= 0 ? vertDist : -vertDist), turnDir));
            var afterVert = path.Segments[^1].EndPoint;
            AddBend(path, afterVert.X, afterVert.Y, turnDir, 180, r);
        }
    }

    private void RouteWestToNorth(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dx < -r && dy > r)
        {
            path.Segments.Add(new StraightSegment(x, y, endX + r, y, 180));
            AddBend(path, endX + r, y, 180, 90, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 90));
        }
        else
        {
            RouteDetourToVertical(path, x, y, endX, endY, dx, dy, r, 180, 90);
        }
    }

    private void RouteWestToEast(RoutedPath path, double x, double y,
                                  double endX, double endY, double dx, double dy, double r)
    {
        RouteOpposite(path, x, y, endX, endY, dy, r, 180, 0);
    }

    private void RouteWestToSouth(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dx < -r && dy < -r)
        {
            path.Segments.Add(new StraightSegment(x, y, endX + r, y, 180));
            AddBend(path, endX + r, y, 180, 270, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 270));
        }
        else
        {
            RouteDetourToVertical(path, x, y, endX, endY, dx, dy, r, 180, 270);
        }
    }

    private void RouteFromSouth(RoutedPath path, double x, double y,
                                 double endX, double endY, int endDir,
                                 double dx, double dy, double r)
    {
        switch (endDir)
        {
            case 270: RouteSouthToSouth(path, x, y, endX, endY, dx, dy, r); break;
            case 0: RouteSouthToEast(path, x, y, endX, endY, dx, dy, r); break;
            case 180: RouteSouthToWest(path, x, y, endX, endY, dx, dy, r); break;
            case 90: RouteSouthToNorth(path, x, y, endX, endY, dx, dy, r); break;
        }
    }

    private void RouteSouthToSouth(RoutedPath path, double x, double y,
                                    double endX, double endY, double dx, double dy, double r)
    {
        if (dy < -2 * r)
        {
            double midY = y + dy / 2;
            path.Segments.Add(new StraightSegment(x, y, x, midY + r, 270));
            int turnDir = dx >= 0 ? 0 : 180;
            AddBend(path, x, midY + r, 270, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b1.X, b1.Y,
                endX + (dx >= 0 ? -r : r), b1.Y, turnDir));
            AddBend(path, endX + (dx >= 0 ? -r : r), b1.Y, turnDir, 270, r);
            var b2 = path.Segments[^1].EndPoint;
            if (Math.Abs(endY - b2.Y) > 0.5)
                path.Segments.Add(new StraightSegment(b2.X, b2.Y, endX, endY, 270));
        }
        else
        {
            double escape = Math.Max(r, Math.Abs(dy) + r);
            path.Segments.Add(new StraightSegment(x, y, x, y - escape, 270));
            int turnDir = dx >= 0 ? 0 : 180;
            AddBend(path, x, y - escape, 270, turnDir, r);
            var b1 = path.Segments[^1].EndPoint;
            double horizDist = Math.Abs(dx) - 2 * r;
            if (horizDist > 0.5)
                path.Segments.Add(new StraightSegment(b1.X, b1.Y,
                    b1.X + (dx >= 0 ? horizDist : -horizDist), b1.Y, turnDir));
            var afterH = path.Segments[^1].EndPoint;
            AddBend(path, afterH.X, afterH.Y, turnDir, 270, r);
        }
    }

    private void RouteSouthToEast(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dy < -r && dx > r)
        {
            path.Segments.Add(new StraightSegment(x, y, x, endY + r, 270));
            AddBend(path, x, endY + r, 270, 0, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 0));
        }
        else
        {
            RouteDetourToHorizontal(path, x, y, endX, endY, dx, dy, r, 270, 0);
        }
    }

    private void RouteSouthToWest(RoutedPath path, double x, double y,
                                   double endX, double endY, double dx, double dy, double r)
    {
        if (dy < -r && dx < -r)
        {
            path.Segments.Add(new StraightSegment(x, y, x, endY + r, 270));
            AddBend(path, x, endY + r, 270, 180, r);
            var b = path.Segments[^1].EndPoint;
            path.Segments.Add(new StraightSegment(b.X, b.Y, endX, endY, 180));
        }
        else
        {
            RouteDetourToHorizontal(path, x, y, endX, endY, dx, dy, r, 270, 180);
        }
    }

    private void RouteSouthToNorth(RoutedPath path, double x, double y,
                                    double endX, double endY, double dx, double dy, double r)
    {
        RouteOppositeVertical(path, x, y, endX, endY, dx, r, 270, 90);
    }

    // --- Shared patterns ---

    /// <summary>
    /// Detour pattern for horizontal start → vertical end when simple L-shape doesn't work.
    /// </summary>
    private void RouteDetourToVertical(RoutedPath path, double x, double y,
                                        double endX, double endY, double dx, double dy,
                                        double r, int startDir, int targetVertDir)
    {
        double escapeX = startDir == 0 ? r : -r;
        path.Segments.Add(new StraightSegment(x, y, x + escapeX, y, startDir));
        int firstTurn = dy >= 0 ? 90 : 270;
        AddBend(path, x + escapeX, y, startDir, firstTurn, r);
        var b1 = path.Segments[^1].EndPoint;
        double targetY = targetVertDir == 90
            ? (dy >= 0 ? Math.Max(endY + r, b1.Y + r) : endY - r)
            : (dy >= 0 ? endY + r : Math.Min(endY - r, b1.Y - r));
        if (Math.Abs(targetY - b1.Y) > 0.5)
            path.Segments.Add(new StraightSegment(b1.X, b1.Y, b1.X, targetY, firstTurn));
        var afterV = path.Segments[^1].EndPoint;
        int hDir = endX > afterV.X ? 0 : 180;
        AddBend(path, afterV.X, afterV.Y, firstTurn, hDir, r);
        var b2 = path.Segments[^1].EndPoint;
        if (Math.Abs(endX - b2.X) > r + 0.5)
            path.Segments.Add(new StraightSegment(b2.X, b2.Y,
                endX - (hDir == 0 ? r : -r), b2.Y, hDir));
        var afterH = path.Segments[^1].EndPoint;
        AddBend(path, afterH.X, afterH.Y, hDir, targetVertDir, r);
        var b3 = path.Segments[^1].EndPoint;
        if (Math.Abs(endY - b3.Y) > 0.5)
            path.Segments.Add(new StraightSegment(b3.X, b3.Y, endX, endY, targetVertDir));
    }

    /// <summary>
    /// Detour pattern for vertical start → horizontal end when simple L-shape doesn't work.
    /// </summary>
    private void RouteDetourToHorizontal(RoutedPath path, double x, double y,
                                          double endX, double endY, double dx, double dy,
                                          double r, int startDir, int targetHorizDir)
    {
        double escapeY = startDir == 90 ? r : -r;
        path.Segments.Add(new StraightSegment(x, y, x, y + escapeY, startDir));
        int turn = dx >= 0 ? 0 : 180;
        AddBend(path, x, y + escapeY, startDir, turn, r);
        var b1 = path.Segments[^1].EndPoint;
        double targetX = targetHorizDir == 0
            ? (dx >= 0 ? Math.Max(endX - r, b1.X + r) : endX + r)
            : (dx >= 0 ? endX - r : Math.Min(endX + r, b1.X - r));
        if (Math.Abs(targetX - b1.X) > 0.5)
            path.Segments.Add(new StraightSegment(b1.X, b1.Y, targetX, b1.Y, turn));
        var b2 = path.Segments[^1].EndPoint;
        int turn2 = endY > b2.Y ? 90 : 270;
        AddBend(path, b2.X, b2.Y, turn, turn2, r);
        var b3 = path.Segments[^1].EndPoint;
        double leaveRoom = turn2 == 90 ? r : -r;
        if (Math.Abs(endY - b3.Y) > r + 0.5)
            path.Segments.Add(new StraightSegment(b3.X, b3.Y,
                b3.X, endY - leaveRoom, turn2));
        var b4 = path.Segments[^1].EndPoint;
        AddBend(path, b4.X, b4.Y, turn2, targetHorizDir, r);
        var b5 = path.Segments[^1].EndPoint;
        if (Math.Abs(endX - b5.X) > 0.5)
            path.Segments.Add(new StraightSegment(b5.X, b5.Y, endX, endY, targetHorizDir));
    }

    /// <summary>
    /// Opposite horizontal directions (East↔West).
    /// </summary>
    private void RouteOpposite(RoutedPath path, double x, double y,
                                double endX, double endY, double dy,
                                double r, int startDir, int endDir)
    {
        double escapeX = startDir == 0 ? r : -r;
        path.Segments.Add(new StraightSegment(x, y, x + escapeX, y, startDir));
        int turn1 = dy >= 0 ? 90 : 270;
        AddBend(path, x + escapeX, y, startDir, turn1, r);
        var p1 = path.Segments[^1].EndPoint;
        double yTarget = dy >= 0
            ? Math.Max(endY + r, p1.Y + r)
            : Math.Min(endY - r, p1.Y - r);
        if (Math.Abs(yTarget - p1.Y) > 0.5)
            path.Segments.Add(new StraightSegment(p1.X, p1.Y, p1.X, yTarget, turn1));
        var p2 = path.Segments[^1].EndPoint;
        AddBend(path, p2.X, p2.Y, turn1, endDir, r);
        var p3 = path.Segments[^1].EndPoint;
        double endApproachX = endDir == 0 ? -r : r;
        if (Math.Abs(endX - p3.X) > r + 0.5)
            path.Segments.Add(new StraightSegment(p3.X, p3.Y,
                endX + endApproachX, p3.Y, endDir));
        var p4 = path.Segments[^1].EndPoint;
        int turn2 = endY > p4.Y ? 90 : 270;
        AddBend(path, p4.X, p4.Y, endDir, turn2, r);
        var p5 = path.Segments[^1].EndPoint;
        double leaveRoom = turn2 == 90 ? -r : r;
        if (Math.Abs(endY - p5.Y) > r + 0.5)
            path.Segments.Add(new StraightSegment(p5.X, p5.Y,
                p5.X, endY + leaveRoom, turn2));
        var p6 = path.Segments[^1].EndPoint;
        AddBend(path, p6.X, p6.Y, turn2, endDir, r);
    }

    /// <summary>
    /// Opposite vertical directions (North↔South).
    /// </summary>
    private void RouteOppositeVertical(RoutedPath path, double x, double y,
                                        double endX, double endY, double dx,
                                        double r, int startDir, int endDir)
    {
        double escapeY = startDir == 90 ? r : -r;
        path.Segments.Add(new StraightSegment(x, y, x, y + escapeY, startDir));
        int t1 = dx >= 0 ? 0 : 180;
        AddBend(path, x, y + escapeY, startDir, t1, r);
        var p1 = path.Segments[^1].EndPoint;
        double xTarget = dx >= 0
            ? Math.Max(endX + r, p1.X + r)
            : Math.Min(endX - r, p1.X - r);
        if (Math.Abs(xTarget - p1.X) > 0.5)
            path.Segments.Add(new StraightSegment(p1.X, p1.Y, xTarget, p1.Y, t1));
        var p2 = path.Segments[^1].EndPoint;
        AddBend(path, p2.X, p2.Y, t1, endDir, r);
        var p3 = path.Segments[^1].EndPoint;
        double endApproachY = endDir == 90 ? -r : r;
        if (Math.Abs(endY - p3.Y) > r + 0.5)
            path.Segments.Add(new StraightSegment(p3.X, p3.Y,
                p3.X, endY + endApproachY, endDir));
        var p4 = path.Segments[^1].EndPoint;
        int t2 = endX > p4.X ? 0 : 180;
        AddBend(path, p4.X, p4.Y, endDir, t2, r);
        var p5 = path.Segments[^1].EndPoint;
        double leaveRoom = t2 == 0 ? -r : r;
        if (Math.Abs(endX - p5.X) > r + 0.5)
            path.Segments.Add(new StraightSegment(p5.X, p5.Y,
                endX + leaveRoom, p5.Y, t2));
        var p6 = path.Segments[^1].EndPoint;
        AddBend(path, p6.X, p6.Y, t2, endDir, r);
    }

    /// <summary>
    /// Adds a 90° bend between two cardinal directions.
    /// </summary>
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
}
