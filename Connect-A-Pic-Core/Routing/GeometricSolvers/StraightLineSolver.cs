using CAP_Core.Components;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.GeometricSolvers;

/// <summary>
/// Attempts to connect two pins with a straight line (zero bends).
/// Only succeeds if pins are perfectly aligned and facing each other.
/// </summary>
public class StraightLineSolver
{
    private readonly WaveguideRouter _router;

    public StraightLineSolver(WaveguideRouter router)
    {
        _router = router;
    }

    /// <summary>
    /// Attempts to connect two pins with a straight line.
    /// </summary>
    /// <returns>A valid RoutedPath if straight connection possible, otherwise null</returns>
    public RoutedPath? TryStraightConnection(PhysicalPin startPin, PhysicalPin endPin)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endAngle = endPin.GetAbsoluteAngle();
        double endEntryAngle = AngleUtilities.NormalizeAngle(endAngle + 180);

        // Check if pins are aligned (angles match or are opposite)
        double angleDiff = Math.Abs(AngleUtilities.NormalizeAngle(startAngle - endEntryAngle));
        if (angleDiff > 5.0) // 5 degree tolerance
            return null;

        // Check if line from start to end is aligned with start angle
        double dx = endX - startX;
        double dy = endY - startY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 1.0) // Too close
            return null;

        double lineAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double lineAngleDiff = Math.Abs(AngleUtilities.NormalizeAngle(lineAngle - startAngle));

        if (lineAngleDiff > 5.0) // Line not aligned with start direction
            return null;

        // Check if path is blocked
        if (_router.PathfindingGrid != null)
        {
            if (_router.PathfindingGrid.IsBlocked((int)startX, (int)startY) ||
                _router.PathfindingGrid.IsBlocked((int)endX, (int)endY))
                return null;

            if (IsLineBlocked(startX, startY, endX, endY))
                return null;
        }

        // Create straight segment
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(startX, startY, endX, endY, startAngle));
        Console.WriteLine("[StraightLineSolver] SUCCESS - straight line connection!");
        return path;
    }

    /// <summary>
    /// Checks if a straight line passes through blocked cells.
    /// </summary>
    private bool IsLineBlocked(double x1, double y1, double x2, double y2)
    {
        if (_router.PathfindingGrid == null) return false;

        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001) return false;

        dx /= length;
        dy /= length;

        double stepSize = _router.PathfindingGrid.CellSizeMicrometers * 0.5;

        for (double t = 0; t < length; t += stepSize)
        {
            double px = x1 + dx * t;
            double py = y1 + dy * t;
            var (gx, gy) = _router.PathfindingGrid.PhysicalToGrid(px, py);
            if (_router.PathfindingGrid.IsBlocked(gx, gy))
                return true;
        }
        return false;
    }
}
