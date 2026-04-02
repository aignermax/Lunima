using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders the background grid, chip boundary, snap grid overlay, and alignment guides.
/// Operates in two phases: background (screen-space) and world (canvas-space).
/// </summary>
public sealed class GridRenderer
{
    /// <summary>
    /// Renders the minor/major grid lines in screen space (before zoom/pan transform).
    /// </summary>
    public void RenderBackground(DrawingContext context, CanvasRenderContext rc)
    {
        DrawGrid(context, rc.Bounds, rc.ViewModel, rc.Zoom);
    }

    /// <summary>
    /// Renders chip boundary, snap grid overlay, and alignment guides in world space.
    /// Call this inside the zoom/pan transform push.
    /// </summary>
    public void RenderWorld(DrawingContext context, CanvasRenderContext rc)
    {
        var vm = rc.ViewModel;
        DrawChipBoundary(context, vm);

        if (vm.GridSnap.IsEnabled)
            DrawSnapGridOverlay(context, vm, rc.Zoom, rc.Bounds);

        if (rc.InteractionState.DraggingComponent != null && vm.AlignmentGuide.IsEnabled && vm.AlignmentGuide.HasAlignments)
            DrawAlignmentGuides(context, vm);
    }

    private static void DrawGrid(DrawingContext context, Rect bounds, DesignCanvasViewModel vm, double zoom)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
        var majorGridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);

        double gridSize = 50 * zoom;
        double majorGridSize = 250 * zoom;
        double offsetX = vm.PanX;
        double offsetY = vm.PanY;

        for (double x = offsetX % gridSize; x < bounds.Width; x += gridSize)
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (double y = offsetY % gridSize; y < bounds.Height; y += gridSize)
            context.DrawLine(gridPen, new Point(0, y), new Point(bounds.Width, y));

        for (double x = offsetX % majorGridSize; x < bounds.Width; x += majorGridSize)
            context.DrawLine(majorGridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (double y = offsetY % majorGridSize; y < bounds.Height; y += majorGridSize)
            context.DrawLine(majorGridPen, new Point(0, y), new Point(bounds.Width, y));
    }

    private static void DrawChipBoundary(DrawingContext context, DesignCanvasViewModel vm)
    {
        var chipRect = new Rect(vm.ChipMinX, vm.ChipMinY,
            vm.ChipMaxX - vm.ChipMinX, vm.ChipMaxY - vm.ChipMinY);

        context.FillRectangle(new SolidColorBrush(Color.FromArgb(20, 100, 150, 255)), chipRect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 100, 150, 255)), 2), chipRect);

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

    private static void DrawCornerMarkers(DrawingContext context, DesignCanvasViewModel vm)
    {
        const double CornerSize = 30.0;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 100, 150, 255)), 3);

        context.DrawLine(pen, new Point(vm.ChipMinX, vm.ChipMinY + CornerSize), new Point(vm.ChipMinX, vm.ChipMinY));
        context.DrawLine(pen, new Point(vm.ChipMinX, vm.ChipMinY), new Point(vm.ChipMinX + CornerSize, vm.ChipMinY));
        context.DrawLine(pen, new Point(vm.ChipMaxX - CornerSize, vm.ChipMinY), new Point(vm.ChipMaxX, vm.ChipMinY));
        context.DrawLine(pen, new Point(vm.ChipMaxX, vm.ChipMinY), new Point(vm.ChipMaxX, vm.ChipMinY + CornerSize));
        context.DrawLine(pen, new Point(vm.ChipMinX, vm.ChipMaxY - CornerSize), new Point(vm.ChipMinX, vm.ChipMaxY));
        context.DrawLine(pen, new Point(vm.ChipMinX, vm.ChipMaxY), new Point(vm.ChipMinX + CornerSize, vm.ChipMaxY));
        context.DrawLine(pen, new Point(vm.ChipMaxX - CornerSize, vm.ChipMaxY), new Point(vm.ChipMaxX, vm.ChipMaxY));
        context.DrawLine(pen, new Point(vm.ChipMaxX, vm.ChipMaxY), new Point(vm.ChipMaxX, vm.ChipMaxY - CornerSize));
    }

    private static void DrawSnapGridOverlay(DrawingContext context, DesignCanvasViewModel vm, double zoom, Rect bounds)
    {
        double gridSize = vm.GridSnap.GridSizeMicrometers;
        if (gridSize <= 0) return;

        var dotBrush = new SolidColorBrush(Color.FromArgb(100, 0, 200, 255));
        const double DotRadius = 1.5;

        double viewMinX = -vm.PanX / zoom;
        double viewMinY = -vm.PanY / zoom;
        double viewMaxX = viewMinX + bounds.Width / zoom;
        double viewMaxY = viewMinY + bounds.Height / zoom;

        double startX = Math.Max(vm.ChipMinX, Math.Floor(viewMinX / gridSize) * gridSize);
        double startY = Math.Max(vm.ChipMinY, Math.Floor(viewMinY / gridSize) * gridSize);
        double endX = Math.Min(vm.ChipMaxX, viewMaxX);
        double endY = Math.Min(vm.ChipMaxY, viewMaxY);

        const int MaxDotsPerAxis = 200;
        double stepX = gridSize;
        double stepY = gridSize;
        if ((int)((endX - startX) / gridSize) + 1 > MaxDotsPerAxis)
            stepX = (endX - startX) / MaxDotsPerAxis;
        if ((int)((endY - startY) / gridSize) + 1 > MaxDotsPerAxis)
            stepY = (endY - startY) / MaxDotsPerAxis;

        for (double x = startX; x <= endX; x += stepX)
        {
            for (double y = startY; y <= endY; y += stepY)
                context.DrawEllipse(dotBrush, null, new Point(x, y), DotRadius, DotRadius);
        }
    }

    private static void DrawAlignmentGuides(DrawingContext context, DesignCanvasViewModel vm)
    {
        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 255, 255)), 1.5)
        {
            DashStyle = new DashStyle(new double[] { 8, 4 }, 0)
        };
        var pinDotBrush = new SolidColorBrush(Color.FromArgb(200, 0, 255, 255));
        const double PinDotRadius = 4.0;

        foreach (var alignment in vm.AlignmentGuide.HorizontalAlignments)
        {
            double y = alignment.YCoordinate;
            var (draggingX, _) = alignment.DraggingPin.GetAbsolutePosition();
            var (alignedX, _) = alignment.AlignedPin.GetAbsolutePosition();
            context.DrawLine(guidePen, new Point(Math.Min(draggingX, alignedX), y), new Point(Math.Max(draggingX, alignedX), y));
            context.DrawEllipse(pinDotBrush, null, new Point(draggingX, y), PinDotRadius, PinDotRadius);
            context.DrawEllipse(pinDotBrush, null, new Point(alignedX, y), PinDotRadius, PinDotRadius);
        }

        foreach (var alignment in vm.AlignmentGuide.VerticalAlignments)
        {
            double x = alignment.XCoordinate;
            var (_, draggingY) = alignment.DraggingPin.GetAbsolutePosition();
            var (_, alignedY) = alignment.AlignedPin.GetAbsolutePosition();
            context.DrawLine(guidePen, new Point(x, Math.Min(draggingY, alignedY)), new Point(x, Math.Max(draggingY, alignedY)));
            context.DrawEllipse(pinDotBrush, null, new Point(x, draggingY), PinDotRadius, PinDotRadius);
            context.DrawEllipse(pinDotBrush, null, new Point(x, alignedY), PinDotRadius, PinDotRadius);
        }
    }
}
