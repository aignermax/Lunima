using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders the pathfinding grid debug overlay and A* path visualization.
/// Only active when <see cref="DesignCanvasViewModel.ShowGridOverlay"/> is enabled.
/// Implements <see cref="ICanvasRenderer"/> for world-space rendering.
/// </summary>
public sealed class PathfindingOverlayRenderer : ICanvasRenderer
{
    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        if (!rc.ViewModel.ShowGridOverlay) return;
        DrawPathfindingGridOverlay(context, rc.ViewModel, rc.Zoom, rc.Bounds);
    }

    private static void DrawPathfindingGridOverlay(DrawingContext context, DesignCanvasViewModel vm, double zoom, Rect bounds)
    {
        var grid = vm.Router.PathfindingGrid;
        if (grid == null) return;

        var componentBlockedBrush = new SolidColorBrush(Color.FromArgb(80, 255, 50, 50));
        var waveguideBlockedBrush = new SolidColorBrush(Color.FromArgb(80, 50, 100, 255));
        double cellSize = grid.CellSizeMicrometers;

        double viewMinX = -vm.PanX / zoom;
        double viewMinY = -vm.PanY / zoom;
        double viewMaxX = viewMinX + bounds.Width / zoom;
        double viewMaxY = viewMinY + bounds.Height / zoom;

        var (gridMinX, gridMinY) = grid.PhysicalToGrid(Math.Max(viewMinX, grid.MinX), Math.Max(viewMinY, grid.MinY));
        var (gridMaxX, gridMaxY) = grid.PhysicalToGrid(Math.Min(viewMaxX, grid.MaxX), Math.Min(viewMaxY, grid.MaxY));

        gridMinX = Math.Max(0, gridMinX);
        gridMinY = Math.Max(0, gridMinY);
        gridMaxX = Math.Min(grid.Width - 1, gridMaxX);
        gridMaxY = Math.Min(grid.Height - 1, gridMaxY);

        int step = CalculateStep(gridMinX, gridMinY, gridMaxX, gridMaxY);

        for (int gx = gridMinX; gx <= gridMaxX; gx += step)
        {
            for (int gy = gridMinY; gy <= gridMaxY; gy += step)
            {
                var (physX, physY) = grid.GridToPhysical(gx, gy);
                var cellRect = new Rect(
                    physX - cellSize * step / 2, physY - cellSize * step / 2,
                    cellSize * step, cellSize * step);

                IBrush? brush = grid.GetCellState(gx, gy) switch
                {
                    1 => componentBlockedBrush,
                    2 => waveguideBlockedBrush,
                    _ => null
                };

                if (brush != null)
                    context.FillRectangle(brush, cellRect);
            }
        }

        DrawAStarPaths(context, vm, grid, cellSize);
        DrawGridInfo(context, vm, grid);
    }

    private static int CalculateStep(int gridMinX, int gridMinY, int gridMaxX, int gridMaxY)
    {
        const int MaxCellsToDraw = 10000;
        int totalCells = (gridMaxX - gridMinX + 1) * (gridMaxY - gridMinY + 1);
        if (totalCells > MaxCellsToDraw)
            return (int)Math.Ceiling(Math.Sqrt((double)totalCells / MaxCellsToDraw));
        return 1;
    }

    private static void DrawAStarPaths(DrawingContext context, DesignCanvasViewModel vm, PathfindingGrid grid, double cellSize)
    {
        var astarPathBrush = new SolidColorBrush(Color.FromArgb(150, 255, 165, 0));
        foreach (var conn in vm.Connections)
        {
            var gridPath = conn.Connection.RoutedPath?.DebugGridPath;
            if (gridPath == null || gridPath.Count == 0) continue;

            foreach (var node in gridPath)
            {
                var (physX, physY) = grid.GridToPhysical(node.X, node.Y);
                context.FillRectangle(astarPathBrush,
                    new Rect(physX - cellSize / 2, physY - cellSize / 2, cellSize, cellSize));
            }
        }
    }

    private static void DrawGridInfo(DrawingContext context, DesignCanvasViewModel vm, PathfindingGrid grid)
    {
        int totalAstarPaths = vm.Connections.Count(c => c.Connection.RoutedPath?.DebugGridPath != null);
        var infoText = new FormattedText(
            $"Grid: {grid.Width}x{grid.Height} cells, {grid.CellSizeMicrometers}µm/cell, {grid.GetBlockedCellCount()} blocked | A* paths: {totalAstarPaths}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            Brushes.Yellow);
        context.DrawText(infoText, new Point(grid.MinX + 10, grid.MinY + 10));
    }
}
