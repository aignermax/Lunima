using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders the canvas background (chip area and adaptive grid), the chip frame, the snap grid
/// overlay and alignment guides. Operates in two phases: background (screen space, before the
/// zoom/pan transform) and world (canvas space).
/// </summary>
public sealed class GridRenderer
{
    private const double CornerTickPx = 14.0;
    private const double DimensionFontPx = 11.0;
    private const double DimensionGapPx = 6.0;
    private const double SnapDotRadiusPx = 1.25;
    private const int MaxDotsPerAxis = 200;
    private const double PinDotRadiusPx = 3.5;

    private static readonly IBrush SnapDotBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.AccentHover, 0x55));
    private static readonly IBrush GuideDotBrush = CanvasPalette.Brush(CanvasPalette.AccentHover);

    /// <summary>
    /// Fills the background and the chip area, then draws the minor/major grid in screen space
    /// (crisp one-pixel lines at any zoom).
    /// </summary>
    public void RenderBackground(DrawingContext context, CanvasRenderContext rc)
    {
        var vm = rc.ViewModel;
        context.FillRectangle(CanvasPalette.BackgroundBrush, rc.Bounds);
        context.FillRectangle(CanvasPalette.ChipFillBrush, ChipScreenRect(vm, rc.Zoom));
        DrawGrid(context, rc.Bounds, vm.PanX, vm.PanY, rc.Zoom);
    }

    /// <summary>
    /// Renders chip frame, snap grid overlay, and alignment guides in world space.
    /// Call this inside the zoom/pan transform push.
    /// </summary>
    public void RenderWorld(DrawingContext context, CanvasRenderContext rc)
    {
        var vm = rc.ViewModel;
        DrawChipFrame(context, vm, rc.Pens, rc.Zoom);

        if (vm.GridSnap.IsEnabled)
            DrawSnapGridOverlay(context, vm, rc.Zoom, rc.Bounds);

        if ((rc.InteractionState.DraggingComponent != null || rc.InteractionState.CrossingAxisDrag != null)
            && vm.AlignmentGuide.IsEnabled && vm.AlignmentGuide.HasAlignments)
            DrawAlignmentGuides(context, vm, rc.Zoom);
    }

    private static Rect ChipWorldRect(DesignCanvasViewModel vm) =>
        new(vm.ChipMinX, vm.ChipMinY, vm.ChipMaxX - vm.ChipMinX, vm.ChipMaxY - vm.ChipMinY);

    private static Rect ChipScreenRect(DesignCanvasViewModel vm, double zoom)
    {
        var world = ChipWorldRect(vm);
        return new Rect(world.X * zoom + vm.PanX, world.Y * zoom + vm.PanY, world.Width * zoom, world.Height * zoom);
    }

    private static void DrawGrid(DrawingContext context, Rect bounds, double panX, double panY, double zoom)
    {
        var spacing = AdaptiveGridSpacing.For(zoom);
        double minorPx = spacing.MinorWorld * zoom;
        if (spacing.MinorOpacity > 0.02)
        {
            var minorColor = CanvasPalette.WithAlpha(CanvasPalette.GridMinor,
                (byte)(CanvasPalette.GridMinor.A * spacing.MinorOpacity));
            DrawLineFamily(context, bounds, panX, panY, minorPx, new ImmutablePen(new ImmutableSolidColorBrush(minorColor)),
                skipEvery: AdaptiveGridSpacing.MinorPerMajor);
        }
        DrawLineFamily(context, bounds, panX, panY, minorPx * AdaptiveGridSpacing.MinorPerMajor,
            new ImmutablePen(new ImmutableSolidColorBrush(CanvasPalette.GridMajor)), skipEvery: 0);
    }

    /// <summary>Draws vertical and horizontal lines every <paramref name="stepPx"/>, snapped to
    /// pixel centers; with <paramref name="skipEvery"/> &gt; 0 every n-th line (a major line) is left out.</summary>
    private static void DrawLineFamily(DrawingContext context, Rect bounds, double panX, double panY,
        double stepPx, IPen pen, int skipEvery)
    {
        for (long k = (long)Math.Ceiling(-panX / stepPx); ; k++)
        {
            double x = panX + k * stepPx;
            if (x > bounds.Width) break;
            if (skipEvery > 0 && k % skipEvery == 0) continue;
            double px = Math.Floor(x) + 0.5;
            context.DrawLine(pen, new Point(px, 0), new Point(px, bounds.Height));
        }
        for (long k = (long)Math.Ceiling(-panY / stepPx); ; k++)
        {
            double y = panY + k * stepPx;
            if (y > bounds.Height) break;
            if (skipEvery > 0 && k % skipEvery == 0) continue;
            double py = Math.Floor(y) + 0.5;
            context.DrawLine(pen, new Point(0, py), new Point(bounds.Width, py));
        }
    }

    private static void DrawChipFrame(DrawingContext context, DesignCanvasViewModel vm, CanvasPens pens, double zoom)
    {
        var chipRect = ChipWorldRect(vm);
        context.DrawRectangle(null, pens.ChipBorder, chipRect);
        DrawCornerTicks(context, chipRect, pens.ChipCorner, CornerTickPx / zoom);

        var dimText = new FormattedText(
            $"{chipRect.Width:0.#} µm × {chipRect.Height:0.#} µm",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            CanvasTypography.Ui,
            DimensionFontPx / zoom,
            CanvasPalette.TextTertiaryBrush);
        context.DrawText(dimText, new Point(chipRect.Left, chipRect.Bottom + DimensionGapPx / zoom));
    }

    private static void DrawCornerTicks(DrawingContext context, Rect r, IPen pen, double len)
    {
        context.DrawLine(pen, new Point(r.Left, r.Top + len), new Point(r.Left, r.Top));
        context.DrawLine(pen, new Point(r.Left, r.Top), new Point(r.Left + len, r.Top));
        context.DrawLine(pen, new Point(r.Right - len, r.Top), new Point(r.Right, r.Top));
        context.DrawLine(pen, new Point(r.Right, r.Top), new Point(r.Right, r.Top + len));
        context.DrawLine(pen, new Point(r.Left, r.Bottom - len), new Point(r.Left, r.Bottom));
        context.DrawLine(pen, new Point(r.Left, r.Bottom), new Point(r.Left + len, r.Bottom));
        context.DrawLine(pen, new Point(r.Right - len, r.Bottom), new Point(r.Right, r.Bottom));
        context.DrawLine(pen, new Point(r.Right, r.Bottom), new Point(r.Right, r.Bottom - len));
    }

    private static void DrawSnapGridOverlay(DrawingContext context, DesignCanvasViewModel vm, double zoom, Rect bounds)
    {
        double gridSize = vm.GridSnap.GridSizeMicrometers;
        if (gridSize <= 0) return;

        double viewMinX = -vm.PanX / zoom;
        double viewMinY = -vm.PanY / zoom;
        double viewMaxX = viewMinX + bounds.Width / zoom;
        double viewMaxY = viewMinY + bounds.Height / zoom;

        double startX = Math.Max(vm.ChipMinX, Math.Floor(viewMinX / gridSize) * gridSize);
        double startY = Math.Max(vm.ChipMinY, Math.Floor(viewMinY / gridSize) * gridSize);
        double endX = Math.Min(vm.ChipMaxX, viewMaxX);
        double endY = Math.Min(vm.ChipMaxY, viewMaxY);

        double stepX = gridSize;
        double stepY = gridSize;
        if ((int)((endX - startX) / gridSize) + 1 > MaxDotsPerAxis)
            stepX = (endX - startX) / MaxDotsPerAxis;
        if ((int)((endY - startY) / gridSize) + 1 > MaxDotsPerAxis)
            stepY = (endY - startY) / MaxDotsPerAxis;

        double radius = SnapDotRadiusPx / zoom;
        for (double x = startX; x <= endX; x += stepX)
        {
            for (double y = startY; y <= endY; y += stepY)
                context.DrawEllipse(SnapDotBrush, null, new Point(x, y), radius, radius);
        }
    }

    private static void DrawAlignmentGuides(DrawingContext context, DesignCanvasViewModel vm, double zoom)
    {
        var guidePen = new ImmutablePen(new ImmutableSolidColorBrush(CanvasPalette.AccentHover), 1.0 / zoom,
            new ImmutableDashStyle(new[] { 6.0, 4.0 }, 0));
        double dotRadius = PinDotRadiusPx / zoom;

        foreach (var alignment in vm.AlignmentGuide.HorizontalAlignments)
        {
            double y = alignment.YCoordinate;
            var (draggingX, _) = alignment.DraggingPin.GetAbsolutePosition();
            var (alignedX, _) = alignment.AlignedPin.GetAbsolutePosition();
            context.DrawLine(guidePen, new Point(Math.Min(draggingX, alignedX), y), new Point(Math.Max(draggingX, alignedX), y));
            context.DrawEllipse(GuideDotBrush, null, new Point(draggingX, y), dotRadius, dotRadius);
            context.DrawEllipse(GuideDotBrush, null, new Point(alignedX, y), dotRadius, dotRadius);
        }

        foreach (var alignment in vm.AlignmentGuide.VerticalAlignments)
        {
            double x = alignment.XCoordinate;
            var (_, draggingY) = alignment.DraggingPin.GetAbsolutePosition();
            var (_, alignedY) = alignment.AlignedPin.GetAbsolutePosition();
            context.DrawLine(guidePen, new Point(x, Math.Min(draggingY, alignedY)), new Point(x, Math.Max(draggingY, alignedY)));
            context.DrawEllipse(GuideDotBrush, null, new Point(x, draggingY), dotRadius, dotRadius);
            context.DrawEllipse(GuideDotBrush, null, new Point(x, alignedY), dotRadius, dotRadius);
        }
    }
}
