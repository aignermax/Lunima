namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// Input for <see cref="MeanderPathGenerator"/>: stretch the route between two poses
/// to a prescribed geometric length, inside a bounding rectangle.
/// Directions follow the <see cref="PathSegment"/> tangent convention (degrees,
/// 0 = +X, counter-clockwise positive); the end direction is the direction of travel
/// at arrival, not the pin's outward normal. The minimum bend radius
/// carries the process floor semantics of ProcessXsection.MinRadiusUm: no arc in the
/// result may have a smaller radius, and the meander's own loops use exactly this radius.
/// <see cref="Obstacles"/> lists what the meander must keep clear of; <see cref="BasePath"/>
/// is the connection's existing route, used as an alternative host when the compact
/// pin-to-pin route would collide.
/// </summary>
public sealed record MeanderRequest(
    double StartX,
    double StartY,
    double StartDirectionDegrees,
    double EndX,
    double EndY,
    double EndDirectionDegrees,
    double TargetLengthMicrometers,
    double ToleranceMicrometers,
    double MinBendRadiusMicrometers,
    MeanderBounds Bounds,
    MeanderObstacles? Obstacles = null,
    RoutedPath? BasePath = null);
