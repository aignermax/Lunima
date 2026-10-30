using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

public partial class WaveguideRouter
{
    /// <summary>Adopts an already built exclusive grid without re-rasterizing regions or retaining worker callbacks.</summary>
    internal void AdoptCompletedGrid(PathfindingGrid? grid,
        IReadOnlyList<(double X, double Y, double Width, double Height)>? regions)
    {
        PathfindingGrid = grid;
        _allowedRoutingRegions = regions;
        _hierarchicalPathfinder = null;
        CostCalculator.DistanceTransformGrid = null;
        if (grid != null) CostCalculator.CellSizeMicrometers = grid.CellSizeMicrometers;
    }
}
