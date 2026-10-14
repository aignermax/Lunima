namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Routing regions of <see cref="PathfindingGrid"/>: on a die assembly a waveguide may only
/// run inside a die — never across the carrier between dies — so every cell whose center lies
/// outside all allowed rectangles is blocked like a component cell. Null (the default) leaves
/// the whole grid routable, exactly as before.
/// </summary>
public partial class PathfindingGrid
{
    private IReadOnlyList<(double X, double Y, double Width, double Height)>? _allowedRegions;

    /// <summary>
    /// Rectangles (µm) routes may use; cells outside all of them are blocked. Applied
    /// immediately and again by every <see cref="RebuildFromComponents"/>.
    /// </summary>
    public IReadOnlyList<(double X, double Y, double Width, double Height)>? AllowedRegions
    {
        get => _allowedRegions;
        set
        {
            _allowedRegions = value;
            BlockCellsOutsideAllowedRegions();
        }
    }

    /// <summary>
    /// Blocks every free cell whose center lies outside all <see cref="AllowedRegions"/>. Each
    /// region is shrunk by half a cell, so a narrow gap between two dies is never bridged by a
    /// cell whose center happens to fall just inside one of them.
    /// </summary>
    private void BlockCellsOutsideAllowedRegions()
    {
        if (_allowedRegions is not { Count: > 0 } regions)
            return;
        double inset = CellSizeMicrometers / 2;
        for (int x = 0; x < Width; x++)
        {
            double cx = MinX + (x + 0.5) * CellSizeMicrometers;
            for (int y = 0; y < Height; y++)
            {
                if (_cells[x, y] != 0)
                    continue;
                double cy = MinY + (y + 0.5) * CellSizeMicrometers;
                if (!IsInsideAnyRegion(regions, cx, cy, inset))
                    _cells[x, y] = 1;
            }
        }
    }

    private static bool IsInsideAnyRegion(
        IReadOnlyList<(double X, double Y, double Width, double Height)> regions, double x, double y, double inset)
    {
        foreach (var region in regions)
        {
            if (x >= region.X + inset && x <= region.X + region.Width - inset
                && y >= region.Y + inset && y <= region.Y + region.Height - inset)
                return true;
        }
        return false;
    }
}
