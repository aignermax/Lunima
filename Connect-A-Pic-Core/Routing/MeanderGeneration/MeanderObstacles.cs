using CAP_Core.Routing.MeanderGeneration.Clearance;

namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// What a meander must keep clear of: the routed paths of the other connections and the
/// footprints of components other than the route's own endpoints. A meander that would cross
/// a route, come closer than <see cref="MinSpacingMicrometers"/> to one outside the pin fan-out
/// zones, touch one anywhere, or enter a footprint is rejected.
/// The geometry is held in a <see cref="ClearanceIndex"/>, built on first use from
/// <see cref="Routes"/>/<see cref="Footprints"/>, or shared (see <see cref="FromIndex"/>) when
/// many routes are matched against one evolving design.
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
    private ClearanceIndex? _index;

    /// <summary>No obstacles: only the bounding rectangle constrains the meander.</summary>
    public static MeanderObstacles None { get; } = new(Array.Empty<RoutedPath>(), Array.Empty<MeanderBounds>(), 0, 0);

    /// <summary>
    /// Obstacles read from a shared index, skipping the routes of <paramref name="ignoredOwners"/>
    /// (the matched connection itself and those sharing one of its pins).
    /// </summary>
    public static MeanderObstacles FromIndex(ClearanceIndex index, IReadOnlySet<object> ignoredOwners,
        double minSpacingMicrometers, double contactClearanceMicrometers)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(ignoredOwners);
        return new MeanderObstacles(Array.Empty<RoutedPath>(), Array.Empty<MeanderBounds>(),
            minSpacingMicrometers, contactClearanceMicrometers)
        {
            _index = index,
            IgnoredOwners = ignoredOwners,
        };
    }

    /// <summary>Indexed routes whose owner is in this set are not obstacles.</summary>
    public IReadOnlySet<object>? IgnoredOwners { get; private init; }

    /// <summary>True when there is nothing to collide with.</summary>
    public bool IsEmpty => _index?.IsEmpty ?? (Routes.Count == 0 && Footprints.Count == 0);

    /// <summary>The spatial index over the obstacle geometry.</summary>
    internal ClearanceIndex Index => _index ??= BuildIndex();

    private ClearanceIndex BuildIndex()
    {
        var index = new ClearanceIndex();
        foreach (var route in Routes)
            index.SetRoute(route, route);
        foreach (var footprint in Footprints)
            index.AddFootprint(footprint);
        return index;
    }
}
