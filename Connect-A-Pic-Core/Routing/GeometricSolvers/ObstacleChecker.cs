using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.Grid;

namespace CAP_Core.Routing.GeometricSolvers;

/// <summary>
/// Checks if paths and segments collide with obstacles in the pathfinding grid.
/// </summary>
public class ObstacleChecker
{
    private readonly PathfindingGrid? _grid;

    public ObstacleChecker(PathfindingGrid? grid)
    {
        _grid = grid;
    }

    /// <summary>
    /// Checks if a straight line segment passes through blocked cells.
    /// </summary>
    public bool IsLineBlocked(double x1, double y1, double x2, double y2)
    {
        if (_grid == null)
            return false;

        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001)
            return false;

        dx /= length;
        dy /= length;

        double stepSize = _grid.CellSizeMicrometers * 0.5;

        for (double t = 0; t < length; t += stepSize)
        {
            double px = x1 + dx * t;
            double py = y1 + dy * t;
            var (gx, gy) = _grid.PhysicalToGrid(px, py);

            if (_grid.IsBlocked(gx, gy))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a bend segment (circular arc) passes through blocked cells.
    /// </summary>
    public bool IsArcBlocked(BendSegment bend)
    {
        if (_grid == null)
            return false;

        // Sample points along the arc
        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
        double arcLength = Math.Abs(sweepRad) * bend.RadiusMicrometers;
        double stepLength = _grid.CellSizeMicrometers * 0.5;
        int numSamples = Math.Max(10, (int)Math.Ceiling(arcLength / stepLength));

        double sign = Math.Sign(bend.SweepAngleDegrees);
        if (sign == 0) sign = 1;

        for (int i = 0; i <= numSamples; i++)
        {
            double t = (double)i / numSamples;
            double angle = startRad + sweepRad * t;
            double px = bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle - Math.PI / 2 * sign);
            double py = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle - Math.PI / 2 * sign);

            var (gx, gy) = _grid.PhysicalToGrid(px, py);

            if (_grid.IsBlocked(gx, gy))
                return true;
        }

        return false;
    }
}
