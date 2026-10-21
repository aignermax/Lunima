using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders all placement previews, drag previews, connection drag preview, and the selection rectangle.
/// Implements <see cref="ICanvasRenderer"/> for world-space rendering.
/// </summary>
public sealed class PreviewRenderer : ICanvasRenderer
{
    private ViewModels.Library.ComponentTemplate? _previewTemplate;
    private ComponentViewModel? _previewCandidate;
    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        var state = rc.InteractionState;
        var vm = rc.ViewModel;

        if (state.ShowPlacementPreview && state.PlacementPreviewTemplate != null)
            DrawPlacementPreview(context, state, vm);

        if (state.ShowGroupTemplatePlacementPreview && state.GroupTemplatePlacementPreview != null)
            DrawGroupTemplatePlacementPreview(context, state, vm);

        if (state.ShowDragPreview && state.DraggingComponent != null)
            DrawDragPreview(context, state);

        if (state.ConnectionDragStartPin != null)
            DrawConnectionPreview(context, state, vm);

        if (vm.Selection.IsBoxSelecting)
            DrawSelectionRectangle(context, vm.Selection);
    }

    private void DrawPlacementPreview(DrawingContext context, CanvasInteractionState state, DesignCanvasViewModel vm)
    {
        var template = state.PlacementPreviewTemplate!;
        double width = template.WidthMicrometers;
        double height = template.HeightMicrometers;
        double x = state.PlacementPreviewPosition.X - width / 2;
        double y = state.PlacementPreviewPosition.Y - height / 2;
        if (!ReferenceEquals(_previewTemplate, template) ||
            !ReferenceEquals(_previewCandidate?.Component.OutlinePolygons, template.OutlinePolygons) ||
            (_previewCandidate is not null && (_previewCandidate.Width != width || _previewCandidate.Height != height)))
        {
            _previewTemplate = template;
            _previewCandidate = template.OutlinePolygons is { Count: > 0 }
                ? new ComponentViewModel(ViewModels.Library.ComponentTemplates.CreateFromTemplate(template, 0, 0)) : null;
        }
        bool canPlace = vm.CanPlaceComponent(x, y, width, height, _previewCandidate);

        var (fillColor, borderColor) = GetPlacementColors(canPlace, 50, 200);
        var rect = new Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(fillColor), rect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), 2), rect);
        context.DrawText(
            new FormattedText(template.Name, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, CanvasTypography.Ui, 10, new SolidColorBrush(borderColor)),
            new Point(x + 5, y + 5));
    }

    private static void DrawGroupTemplatePlacementPreview(DrawingContext context, CanvasInteractionState state, DesignCanvasViewModel vm)
    {
        var template = state.GroupTemplatePlacementPreview!;
        double width = template.WidthMicrometers;
        double height = template.HeightMicrometers;
        double x = state.GroupTemplatePlacementPreviewPosition.X - width / 2;
        double y = state.GroupTemplatePlacementPreviewPosition.Y - height / 2;
        bool canPlace = vm.CanPlaceComponent(x, y, width, height);

        var (fillColor, borderColor) = GetGroupPlacementColors(canPlace);
        var rect = new Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(fillColor), rect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), 2), rect);
        context.DrawText(
            new FormattedText(template.Name, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, CanvasTypography.UiSemiBold, 12,
                new SolidColorBrush(borderColor)),
            new Point(x + 5, y + 5));
        context.DrawText(
            new FormattedText($"{template.ComponentCount} components | {width:F0}×{height:F0}µm",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                CanvasTypography.Ui, 9, new SolidColorBrush(borderColor)),
            new Point(x + 5, y + height - 15));
    }

    private static void DrawDragPreview(DrawingContext context, CanvasInteractionState state)
    {
        var comp = state.DraggingComponent!;
        var (fillColor, borderColor) = GetPlacementColors(state.DragPreviewValid, 50, 200);
        var rect = new Rect(state.DragPreviewPosition.X, state.DragPreviewPosition.Y, comp.Width, comp.Height);
        context.FillRectangle(new SolidColorBrush(fillColor), rect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), 2), rect);
    }

    private static void DrawConnectionPreview(DrawingContext context, CanvasInteractionState state, DesignCanvasViewModel vm)
    {
        var startPin = state.ConnectionDragStartPin!;
        var (startX, startY) = startPin.GetAbsolutePosition();
        var targetPin = vm.HighlightedPin?.Pin;
        // Same-component pins are valid targets (feedback loops); only the
        // start pin itself is not.
        bool isValidTarget = targetPin != null && targetPin != startPin;

        var pen = isValidTarget
            ? new Pen(CanvasPalette.AccentHoverBrush, 2)
            : new Pen(CanvasPalette.TextTertiaryBrush, 2) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };

        var endPoint = isValidTarget
            ? new Point(targetPin!.GetAbsolutePosition().x, targetPin.GetAbsolutePosition().y)
            : state.ConnectionDragCurrentPoint;

        context.DrawLine(pen, new Point(startX, startY), endPoint);
        context.DrawEllipse(CanvasPalette.AccentHoverBrush, null, new Point(startX, startY), 5, 5);
        if (isValidTarget)
            context.DrawEllipse(CanvasPalette.AccentHoverBrush, null, endPoint, 5, 5);
    }

    private static void DrawSelectionRectangle(DrawingContext context, SelectionManager selection)
    {
        var (minX, minY, maxX, maxY) = selection.GetNormalizedBox();
        double width = maxX - minX;
        double height = maxY - minY;
        if (width < 1 && height < 1) return;

        var rect = new Rect(minX, minY, width, height);
        context.FillRectangle(CanvasPalette.SelectionBoxFillBrush, rect);
        context.DrawRectangle(null,
            new Pen(CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.AccentHover, 200)), 1.5)
            {
                DashStyle = new DashStyle(new double[] { 6, 3 }, 0)
            }, rect);
    }

    private static (Color fill, Color border) GetPlacementColors(bool canPlace, byte fillAlpha, byte borderAlpha)
    {
        var hue = canPlace ? CanvasPalette.Accent : CanvasPalette.Danger;
        var fill = CanvasPalette.WithAlpha(hue, fillAlpha);
        var border = CanvasPalette.WithAlpha(hue, borderAlpha);
        return (fill, border);
    }

    private static (Color fill, Color border) GetGroupPlacementColors(bool canPlace)
    {
        var hue = canPlace ? CanvasPalette.AccentHover : CanvasPalette.Danger;
        var fill = CanvasPalette.WithAlpha(hue, 50);
        var border = CanvasPalette.WithAlpha(hue, 200);
        return (fill, border);
    }
}
