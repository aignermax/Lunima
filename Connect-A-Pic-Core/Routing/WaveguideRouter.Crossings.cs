using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Routing;

/// <summary>
/// How a crossing-aware route may cross other waveguides: the crossing component's edge,
/// the straight run kept beyond its ports, and what one crossing costs the search.
/// </summary>
/// <param name="CrossingEdgeMicrometers">Edge length of the crossing component (µm).</param>
/// <param name="ClearanceMicrometers">Straight run kept beyond the crossing ports on both wires (µm).</param>
/// <param name="PenaltyCost">Search cost of one crossing in µm of equivalent path length.</param>
public sealed record CrossingRouteSettings(double CrossingEdgeMicrometers, double ClearanceMicrometers, double PenaltyCost);

/// <summary>
/// The crossing-aware half of <see cref="WaveguideRouter"/>: with <see cref="CrossingRouting"/>
/// set, the A* attempts may jump straight across other waveguides where a crossing component
/// fits, and <see cref="LastPlannedCrossings"/> lists where. Off by default — every other
/// route stays the classic avoid-only search.
/// </summary>
public partial class WaveguideRouter
{
    /// <summary>
    /// Allows routes to cross other waveguides where a crossing fits (null = avoid-only).
    /// Set only for a crossing pass: crossings are real components the caller must place.
    /// </summary>
    public CrossingRouteSettings? CrossingRouting { get; set; }

    /// <summary>The crossings the most recent successful route planned (empty when none).</summary>
    public IReadOnlyList<PlannedCrossing> LastPlannedCrossings { get; private set; } = Array.Empty<PlannedCrossing>();

    /// <summary>The crossing move for the current grid, or null while crossing routing is off.</summary>
    private CrossingStep? CreateCrossingStep() =>
        CrossingRouting is { } settings && PathfindingGrid != null
            ? new CrossingStep(PathfindingGrid, settings.CrossingEdgeMicrometers, settings.ClearanceMicrometers, settings.PenaltyCost)
            : null;

    /// <summary>Records the crossings the grid path jumped through.</summary>
    private void RecordPlannedCrossings(List<AStarNode>? gridPath) =>
        LastPlannedCrossings = gridPath?
            .Where(node => node.Crossing.HasValue)
            .Select(node => node.Crossing!.Value)
            .ToList() ?? (IReadOnlyList<PlannedCrossing>)Array.Empty<PlannedCrossing>();
}
