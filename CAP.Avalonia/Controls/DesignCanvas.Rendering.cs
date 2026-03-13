using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Visualization;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Rendering methods for DesignCanvas.
/// Contains all Draw* methods extracted from the main DesignCanvas class.
/// </summary>
public partial class DesignCanvas
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var vm = ViewModel;
        if (vm == null) return;

        // Background
        context.FillRectangle(Brushes.Black, bounds);

        // Draw grid
        DrawGrid(context, bounds);

        // Apply zoom and pan transform
        using (context.PushTransform(Matrix.CreateTranslation(vm.PanX, vm.PanY)))
        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom)))
        {
            DrawChipBoundary(context, vm);

            if (vm.GridSnap.IsEnabled)
            {
                DrawSnapGridOverlay(context, vm);
            }

            if (vm.ShowGridOverlay)
            {
                DrawPathfindingGridOverlay(context, vm);
            }

            // Draw connections first (behind components)
            // Skip connections that are internal to groups (they're rendered as frozen paths inside the group)
            var allGroups = WaveguideFilteringHelper.CollectAllGroups(vm.Components.Select(c => c.Component));
            foreach (var conn in vm.Connections)
            {
                if (!WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(conn.Connection, allGroups))
                {
                    DrawWaveguideConnection(context, conn, vm);
                }
            }

            // Draw power label on hovered connection
            if (vm.ShowPowerFlow && _interactionState.HoveredConnection != null)
            {
                DrawPowerHoverLabel(context, _interactionState.HoveredConnection, vm);
            }

            // Draw components
            foreach (var comp in vm.Components)
            {
                DrawComponent(context, comp);
            }

            // Draw component placement preview
            if (_interactionState.ShowPlacementPreview && _interactionState.PlacementPreviewTemplate != null)
            {
                DrawPlacementPreview(context, vm);
            }

            // Draw drag preview
            if (_interactionState.ShowDragPreview && _interactionState.DraggingComponent != null)
            {
                DrawDragPreview(context);
            }

            // Draw connection drag preview
            if (_interactionState.ConnectionDragStartPin != null)
            {
                DrawConnectionPreview(context);
            }

            // Draw alignment guide helper lines
            if (_interactionState.DraggingComponent != null && vm.AlignmentGuide.IsEnabled && vm.AlignmentGuide.HasAlignments)
            {
                DrawAlignmentGuides(context, vm);
            }

            // Draw box-selection rectangle
            if (vm.Selection.IsBoxSelecting)
            {
                DrawSelectionRectangle(context, vm.Selection);
            }
        }

        // Draw mode indicator
        DrawModeIndicator(context, bounds);

        // Draw status info
        DrawStatusInfo(context, bounds);
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
        var majorGridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);

        double gridSize = 50 * Zoom;
        double majorGridSize = 250 * Zoom;

        var vm = ViewModel;
        double offsetX = vm?.PanX ?? 0;
        double offsetY = vm?.PanY ?? 0;

        // Minor grid
        for (double x = offsetX % gridSize; x < bounds.Width; x += gridSize)
        {
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, bounds.Height));
        }
        for (double y = offsetY % gridSize; y < bounds.Height; y += gridSize)
        {
            context.DrawLine(gridPen, new Point(0, y), new Point(bounds.Width, y));
        }

        // Major grid (250µm = 1 tile equivalent)
        for (double x = offsetX % majorGridSize; x < bounds.Width; x += majorGridSize)
        {
            context.DrawLine(majorGridPen, new Point(x, 0), new Point(x, bounds.Height));
        }
        for (double y = offsetY % majorGridSize; y < bounds.Height; y += majorGridSize)
        {
            context.DrawLine(majorGridPen, new Point(0, y), new Point(bounds.Width, y));
        }
    }

    private void DrawChipBoundary(DrawingContext context, DesignCanvasViewModel vm)
    {
        var chipRect = new Rect(vm.ChipMinX, vm.ChipMinY,
            vm.ChipMaxX - vm.ChipMinX, vm.ChipMaxY - vm.ChipMinY);

        var chipFill = new SolidColorBrush(Color.FromArgb(20, 100, 150, 255));
        context.FillRectangle(chipFill, chipRect);

        var chipBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 100, 150, 255)), 2);
        context.DrawRectangle(null, chipBorderPen, chipRect);

        DrawCornerMarkers(context, vm);

        var dimText = new FormattedText(
            $"{vm.ChipMaxX - vm.ChipMinX}µm × {vm.ChipMaxY - vm.ChipMinY}µm",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            new SolidColorBrush(Color.FromArgb(150, 100, 150, 255)));

        context.DrawText(dimText, new Point(vm.ChipMinX + 5, vm.ChipMaxY + 5));
    }

    private void DrawCornerMarkers(DrawingContext context, DesignCanvasViewModel vm)
    {
        double cornerSize = 30.0;
        var cornerPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 100, 150, 255)), 3);

        // Top-left
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMinY + cornerSize), new Point(vm.ChipMinX, vm.ChipMinY));
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMinY), new Point(vm.ChipMinX + cornerSize, vm.ChipMinY));

        // Top-right
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX - cornerSize, vm.ChipMinY), new Point(vm.ChipMaxX, vm.ChipMinY));
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX, vm.ChipMinY), new Point(vm.ChipMaxX, vm.ChipMinY + cornerSize));

        // Bottom-left
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMaxY - cornerSize), new Point(vm.ChipMinX, vm.ChipMaxY));
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMaxY), new Point(vm.ChipMinX + cornerSize, vm.ChipMaxY));

        // Bottom-right
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX - cornerSize, vm.ChipMaxY), new Point(vm.ChipMaxX, vm.ChipMaxY));
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX, vm.ChipMaxY), new Point(vm.ChipMaxX, vm.ChipMaxY - cornerSize));
    }

    private void DrawSnapGridOverlay(DrawingContext context, DesignCanvasViewModel vm)
    {
        double gridSize = vm.GridSnap.GridSizeMicrometers;
        if (gridSize <= 0) return;

        var dotBrush = new SolidColorBrush(Color.FromArgb(100, 0, 200, 255));
        double dotRadius = 1.5;

        double viewMinX = -vm.PanX / Zoom;
        double viewMinY = -vm.PanY / Zoom;
        double viewMaxX = viewMinX + Bounds.Width / Zoom;
        double viewMaxY = viewMinY + Bounds.Height / Zoom;

        double startX = Math.Max(vm.ChipMinX, Math.Floor(viewMinX / gridSize) * gridSize);
        double startY = Math.Max(vm.ChipMinY, Math.Floor(viewMinY / gridSize) * gridSize);
        double endX = Math.Min(vm.ChipMaxX, viewMaxX);
        double endY = Math.Min(vm.ChipMaxY, viewMaxY);

        const int MaxDotsPerAxis = 200;
        double stepX = gridSize;
        double stepY = gridSize;
        int countX = (int)((endX - startX) / gridSize) + 1;
        int countY = (int)((endY - startY) / gridSize) + 1;
        if (countX > MaxDotsPerAxis)
            stepX = (endX - startX) / MaxDotsPerAxis;
        if (countY > MaxDotsPerAxis)
            stepY = (endY - startY) / MaxDotsPerAxis;

        for (double x = startX; x <= endX; x += stepX)
        {
            for (double y = startY; y <= endY; y += stepY)
            {
                context.DrawEllipse(dotBrush, null, new Point(x, y), dotRadius, dotRadius);
            }
        }
    }

    private void DrawPathfindingGridOverlay(DrawingContext context, DesignCanvasViewModel vm)
    {
        var grid = vm.Router.PathfindingGrid;
        if (grid == null) return;

        var componentBlockedBrush = new SolidColorBrush(Color.FromArgb(80, 255, 50, 50));
        var waveguideBlockedBrush = new SolidColorBrush(Color.FromArgb(80, 50, 100, 255));

        double cellSize = grid.CellSizeMicrometers;

        double viewMinX = -vm.PanX / Zoom;
        double viewMinY = -vm.PanY / Zoom;
        double viewMaxX = viewMinX + Bounds.Width / Zoom;
        double viewMaxY = viewMinY + Bounds.Height / Zoom;

        var (gridMinX, gridMinY) = grid.PhysicalToGrid(Math.Max(viewMinX, grid.MinX), Math.Max(viewMinY, grid.MinY));
        var (gridMaxX, gridMaxY) = grid.PhysicalToGrid(Math.Min(viewMaxX, grid.MaxX), Math.Min(viewMaxY, grid.MaxY));

        gridMinX = Math.Max(0, gridMinX);
        gridMinY = Math.Max(0, gridMinY);
        gridMaxX = Math.Min(grid.Width - 1, gridMaxX);
        gridMaxY = Math.Min(grid.Height - 1, gridMaxY);

        int maxCellsToDraw = 10000;
        int step = 1;
        int totalCells = (gridMaxX - gridMinX + 1) * (gridMaxY - gridMinY + 1);
        if (totalCells > maxCellsToDraw)
        {
            step = (int)Math.Ceiling(Math.Sqrt((double)totalCells / maxCellsToDraw));
        }

        for (int gx = gridMinX; gx <= gridMaxX; gx += step)
        {
            for (int gy = gridMinY; gy <= gridMaxY; gy += step)
            {
                var (physX, physY) = grid.GridToPhysical(gx, gy);
                var cellRect = new Rect(
                    physX - cellSize * step / 2,
                    physY - cellSize * step / 2,
                    cellSize * step,
                    cellSize * step);

                byte cellState = grid.GetCellState(gx, gy);

                IBrush? brush = cellState switch
                {
                    1 => componentBlockedBrush,
                    2 => waveguideBlockedBrush,
                    _ => null
                };

                if (brush != null)
                {
                    context.FillRectangle(brush, cellRect);
                }
            }
        }

        DrawAStarPaths(context, vm, grid, cellSize);
        DrawGridInfo(context, vm, grid);
    }

    private void DrawAStarPaths(DrawingContext context, DesignCanvasViewModel vm, PathfindingGrid grid, double cellSize)
    {
        var astarPathBrush = new SolidColorBrush(Color.FromArgb(150, 255, 165, 0));
        foreach (var conn in vm.Connections)
        {
            var gridPath = conn.Connection.RoutedPath?.DebugGridPath;
            if (gridPath != null && gridPath.Count > 0)
            {
                foreach (var node in gridPath)
                {
                    var (physX, physY) = grid.GridToPhysical(node.X, node.Y);
                    var cellRect = new Rect(
                        physX - cellSize / 2,
                        physY - cellSize / 2,
                        cellSize,
                        cellSize);
                    context.FillRectangle(astarPathBrush, cellRect);
                }
            }
        }
    }

    private void DrawGridInfo(DrawingContext context, DesignCanvasViewModel vm, PathfindingGrid grid)
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

    private void DrawAlignmentGuides(DrawingContext context, DesignCanvasViewModel vm)
    {
        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 255, 255)), 1.5)
        {
            DashStyle = new DashStyle(new double[] { 8, 4 }, 0)
        };

        var pinDotBrush = new SolidColorBrush(Color.FromArgb(200, 0, 255, 255));
        const double pinDotRadius = 4.0;

        // Draw horizontal alignment lines (same Y coordinate)
        foreach (var alignment in vm.AlignmentGuide.HorizontalAlignments)
        {
            double y = alignment.YCoordinate;
            var (draggingX, _) = alignment.DraggingPin.GetAbsolutePosition();
            var (alignedX, _) = alignment.AlignedPin.GetAbsolutePosition();

            // Draw line spanning from left pin to right pin
            double minX = Math.Min(draggingX, alignedX);
            double maxX = Math.Max(draggingX, alignedX);
            context.DrawLine(guidePen, new Point(minX, y), new Point(maxX, y));

            // Draw dots at pin positions
            context.DrawEllipse(pinDotBrush, null, new Point(draggingX, y), pinDotRadius, pinDotRadius);
            context.DrawEllipse(pinDotBrush, null, new Point(alignedX, y), pinDotRadius, pinDotRadius);
        }

        // Draw vertical alignment lines (same X coordinate)
        foreach (var alignment in vm.AlignmentGuide.VerticalAlignments)
        {
            double x = alignment.XCoordinate;
            var (_, draggingY) = alignment.DraggingPin.GetAbsolutePosition();
            var (_, alignedY) = alignment.AlignedPin.GetAbsolutePosition();

            // Draw line spanning from top pin to bottom pin
            double minY = Math.Min(draggingY, alignedY);
            double maxY = Math.Max(draggingY, alignedY);
            context.DrawLine(guidePen, new Point(x, minY), new Point(x, maxY));

            // Draw dots at pin positions
            context.DrawEllipse(pinDotBrush, null, new Point(x, draggingY), pinDotRadius, pinDotRadius);
            context.DrawEllipse(pinDotBrush, null, new Point(x, alignedY), pinDotRadius, pinDotRadius);
        }
    }
}
