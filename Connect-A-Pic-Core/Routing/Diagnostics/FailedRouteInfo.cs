using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Captures diagnostic information about a single failed routing attempt.
/// </summary>
public class FailedRouteInfo
{
    /// <summary>
    /// Name or ID of the start pin.
    /// </summary>
    public string StartPinName { get; set; } = "";

    /// <summary>
    /// Name or ID of the end pin.
    /// </summary>
    public string EndPinName { get; set; } = "";

    /// <summary>
    /// Start position in physical coordinates (micrometers).
    /// </summary>
    public (double X, double Y) StartPosition { get; set; }

    /// <summary>
    /// End position in physical coordinates (micrometers).
    /// </summary>
    public (double X, double Y) EndPosition { get; set; }

    /// <summary>
    /// Start pin angle in degrees.
    /// </summary>
    public double StartAngleDegrees { get; set; }

    /// <summary>
    /// End pin angle in degrees.
    /// </summary>
    public double EndAngleDegrees { get; set; }

    /// <summary>
    /// Human-readable reason the route failed.
    /// </summary>
    public string FailureReason { get; set; } = "";

    /// <summary>
    /// A* search statistics, if A* was attempted.
    /// </summary>
    public AStarSearchStats? SearchStats { get; set; }

    /// <summary>
    /// Pin alignment information describing the geometric relationship.
    /// </summary>
    public PinAlignmentInfo? PinAlignment { get; set; }

    /// <summary>
    /// Obstacle map cells around the failed connection area.
    /// </summary>
    public List<ObstacleCell> ObstacleMap { get; set; } = new();
}
