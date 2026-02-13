using CAP_Core.Grid;

namespace CAP_Core.Components;

/// <summary>
/// Computes usage statistics for components placed on a grid.
/// Counts instances per component type and provides sorted results.
/// </summary>
public class LibraryStatistics
{
    private readonly ITileManager _tileManager;

    /// <summary>
    /// Creates a new LibraryStatistics instance.
    /// </summary>
    /// <param name="tileManager">The tile manager to read components from.</param>
    public LibraryStatistics(ITileManager tileManager)
    {
        _tileManager = tileManager ?? throw new ArgumentNullException(nameof(tileManager));
    }

    /// <summary>
    /// Counts instances of each component type on the grid,
    /// sorted by usage count in descending order.
    /// </summary>
    /// <returns>A list of usage records sorted by count descending.</returns>
    public List<ComponentUsageRecord> GetUsageStatistics()
    {
        var components = _tileManager.GetAllComponents();

        return components
            .GroupBy(c => c.TypeNumber)
            .Select(group => new ComponentUsageRecord(
                identifier: group.First().Identifier,
                typeNumber: group.Key,
                count: group.Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Identifier)
            .ToList();
    }

    /// <summary>
    /// Gets the total number of placed components on the grid.
    /// </summary>
    /// <returns>The total component count.</returns>
    public int GetTotalComponentCount()
    {
        return _tileManager.GetAllComponents().Count;
    }

    /// <summary>
    /// Gets the number of distinct component types used on the grid.
    /// </summary>
    /// <returns>The number of unique component types.</returns>
    public int GetDistinctTypeCount()
    {
        return _tileManager.GetAllComponents()
            .Select(c => c.TypeNumber)
            .Distinct()
            .Count();
    }
}
