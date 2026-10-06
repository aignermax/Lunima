namespace CAP_Core.Routing.ImportedGeometry;

/// <summary>How faithfully a <see cref="RibbonFit"/> reproduces its source polygon.</summary>
public enum RibbonFitKind
{
    /// <summary>The polygon is a straight ribbon; the centerline is one exact straight segment.</summary>
    Straight,

    /// <summary>The polygon is a circular-arc ribbon; the centerline is one exact bend segment.</summary>
    Arc,

    /// <summary>
    /// The polygon is a ribbon of some other curvature (e.g. an Euler bend); the
    /// centerline is a chain of short straights through the side midpoints.
    /// </summary>
    Polyline,
}

/// <summary>
/// The centerline recovered from one drawn waveguide polygon: a ribbon with two
/// short end caps and two long sides. Segments run from the midpoint of the first
/// cap to the midpoint of the second; which cap is "first" is arbitrary until
/// <see cref="FrozenRouteChainer"/> orients the piece inside a route.
/// </summary>
/// <param name="Segments">Centerline segments, connected end to start.</param>
/// <param name="WidthMicrometers">Ribbon width (mean cap length, or radius difference for arcs).</param>
/// <param name="Kind">Whether the centerline is an exact straight/arc or a polyline approximation.</param>
public sealed record RibbonFit(IReadOnlyList<PathSegment> Segments, double WidthMicrometers, RibbonFitKind Kind)
{
    /// <summary>Centerline start point (midpoint of the first cap).</summary>
    public (double X, double Y) Start => Segments[0].StartPoint;

    /// <summary>Centerline end point (midpoint of the second cap).</summary>
    public (double X, double Y) End => Segments[^1].EndPoint;

    /// <summary>Returns the same centerline traversed from <see cref="End"/> to <see cref="Start"/>.</summary>
    public RibbonFit Reversed() =>
        this with { Segments = Segments.Reverse().Select(PathSegmentReversal.Reverse).ToList() };
}
