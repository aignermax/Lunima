using CAP_Core.Components;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Collects routing diagnostics for failed connections.
/// Records failure information and triggers export when routing fails.
/// </summary>
public class RoutingDiagnosticsCollector
{
    private readonly List<FailedRouteInfo> _failedRoutes = new();

    /// <summary>
    /// All failed routes recorded in the current session.
    /// </summary>
    public IReadOnlyList<FailedRouteInfo> FailedRoutes => _failedRoutes;

    /// <summary>
    /// Records a routing failure with full diagnostic information.
    /// </summary>
    /// <param name="startPin">The start physical pin.</param>
    /// <param name="endPin">The end physical pin.</param>
    /// <param name="failureReason">Human-readable failure reason.</param>
    /// <param name="searchStats">A* search stats, if A* was attempted.</param>
    /// <param name="grid">The pathfinding grid (for obstacle extraction).</param>
    public FailedRouteInfo RecordFailure(
        PhysicalPin startPin,
        PhysicalPin endPin,
        string failureReason,
        AStarSearchStats? searchStats,
        PathfindingGrid? grid)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endAngle = endPin.GetAbsoluteAngle();
        double endInputAngle = NormalizeAngle(endAngle + 180);

        var alignment = PinAlignmentAnalyzer.Analyze(
            startX, startY, startAngle,
            endX, endY, endInputAngle);

        var obstacleMap = grid != null
            ? ObstacleMapExtractor.Extract(grid, startX, startY, endX, endY)
            : new List<ObstacleCell>();

        var info = new FailedRouteInfo
        {
            StartPinName = startPin.Name ?? "unknown",
            EndPinName = endPin.Name ?? "unknown",
            StartPosition = (startX, startY),
            EndPosition = (endX, endY),
            StartAngleDegrees = startAngle,
            EndAngleDegrees = endInputAngle,
            FailureReason = failureReason,
            SearchStats = searchStats,
            PinAlignment = alignment,
            ObstacleMap = obstacleMap
        };

        _failedRoutes.Add(info);
        return info;
    }

    /// <summary>
    /// Clears all recorded diagnostics.
    /// </summary>
    public void Clear()
    {
        _failedRoutes.Clear();
    }

    /// <summary>
    /// Normalizes angle to 0-360 range.
    /// </summary>
    private static double NormalizeAngle(double degrees)
    {
        while (degrees < 0) degrees += 360;
        while (degrees >= 360) degrees -= 360;
        return degrees;
    }
}
