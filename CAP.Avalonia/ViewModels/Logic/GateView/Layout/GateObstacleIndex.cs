using Avalonia;

namespace CAP.Avalonia.ViewModels.Logic.GateView.Layout;

/// <summary>
/// Symbol footprints bucketed on a coarse grid, so asking "does this wire run cross a
/// symbol?" looks at the few symbols nearby instead of all of them — a CPU-sized design
/// has hundreds of symbols and thousands of runs, and the scene is re-laid out while a
/// gate is dragged.
/// </summary>
public sealed class GateObstacleIndex
{
    private const double CellSize = 1000;

    private readonly Dictionary<(long, long), List<Rect>> _cells = new();

    /// <summary>Indexes the given footprints.</summary>
    public GateObstacleIndex(IEnumerable<Rect> footprints)
    {
        foreach (var footprint in footprints)
        {
            foreach (var cell in CellsOf(footprint))
            {
                if (!_cells.TryGetValue(cell, out var list))
                    _cells[cell] = list = new List<Rect>();
                list.Add(footprint);
            }
        }
    }

    /// <summary>Every footprint whose grid cells overlap <paramref name="area"/> (may repeat a footprint).</summary>
    public IEnumerable<Rect> Near(Rect area)
    {
        foreach (var cell in CellsOf(area))
        {
            if (_cells.TryGetValue(cell, out var list))
            {
                foreach (var footprint in list)
                    yield return footprint;
            }
        }
    }

    private static IEnumerable<(long, long)> CellsOf(Rect area)
    {
        var x0 = (long)Math.Floor(area.Left / CellSize);
        var x1 = (long)Math.Floor(area.Right / CellSize);
        var y0 = (long)Math.Floor(area.Top / CellSize);
        var y1 = (long)Math.Floor(area.Bottom / CellSize);
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
                yield return (x, y);
        }
    }
}
