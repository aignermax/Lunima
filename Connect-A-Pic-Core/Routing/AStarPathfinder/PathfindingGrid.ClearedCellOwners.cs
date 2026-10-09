namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Which waveguides a routing attempt opened up: pin corridors and fan-out lines clear
/// waveguide cells near a pin so the search can start and arrive, and the router checks the
/// finished route against exactly those waveguides' real geometry.
/// </summary>
public partial class PathfindingGrid
{
    /// <summary>
    /// The registered waveguides owning any of the cleared cells that were waveguide cells
    /// before clearing, with their exact geometry.
    /// </summary>
    /// <param name="clearedSets">Cell sets returned by the clear methods (cell → original state).</param>
    internal List<(Guid Owner, RoutedPath Path)> WaveguidesOwningClearedCells(
        params Dictionary<(int x, int y), byte>[] clearedSets)
    {
        var cleared = clearedSets.SelectMany(set => set.Where(kv => kv.Value == 2).Select(kv => kv.Key)).ToHashSet();
        var owners = new List<(Guid, RoutedPath)>();
        if (cleared.Count == 0) return owners;
        lock (_waveguideCellsLock)
        {
            foreach (var (owner, cells) in _waveguideCells)
            {
                if (!cleared.Any(cells.Contains) || !_waveguideGeometry.TryGetValue(owner, out var geometry))
                    continue;
                var path = new RoutedPath();
                path.Segments.AddRange(geometry);
                owners.Add((owner, path));
            }
        }
        return owners;
    }
}
