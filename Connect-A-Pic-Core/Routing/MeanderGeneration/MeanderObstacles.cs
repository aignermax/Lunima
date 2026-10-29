namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// What a meander must keep clear of: the routed paths of the other connections and the
/// footprints of components other than the route's own endpoints. A meander that would cross
/// a route, come closer than <see cref="MinSpacingMicrometers"/> to one outside the pin fan-out
/// zones, touch one anywhere, or enter a footprint is rejected.
/// </summary>
/// <param name="Routes">Routed paths of all other connections.</param>
/// <param name="Footprints">Component footprints the meander may not enter.</param>
/// <param name="MinSpacingMicrometers">Required centerline spacing to other routes.</param>
/// <param name="ContactClearanceMicrometers">Centerline distance below which two routes physically merge.</param>
public sealed record MeanderObstacles(
    IReadOnlyList<RoutedPath> Routes,
    IReadOnlyList<MeanderBounds> Footprints,
    double MinSpacingMicrometers,
    double ContactClearanceMicrometers)
{
    /// <summary>No obstacles: only the bounding rectangle constrains the meander.</summary>
    public static MeanderObstacles None { get; } = new(Array.Empty<RoutedPath>(), Array.Empty<MeanderBounds>(), 0, 0);

    /// <summary>True when there is nothing to collide with.</summary>
    public bool IsEmpty => Routes.Count == 0 && Footprints.Count == 0;
}
