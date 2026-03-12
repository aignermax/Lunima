using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Preview and UI overlay rendering methods for DesignCanvas.
/// </summary>
public partial class DesignCanvas
{
    private void DrawPlacementPreview(DrawingContext context, DesignCanvasViewModel vm)
    {
        if (_interactionState.PlacementPreviewTemplate == null) return;

        double width = _interactionState.PlacementPreviewTemplate.WidthMicrometers;
        double height = _interactionState.PlacementPreviewTemplate.HeightMicrometers;
        double x = _interactionState.PlacementPreviewPosition.X - width / 2;
        double y = _interactionState.PlacementPreviewPosition.Y - height / 2;

        bool canPlace = vm.CanPlaceComponent(x, y, width, height);

        var fillColor = canPlace
            ? Color.FromArgb(50, 100, 255, 100)
            : Color.FromArgb(50, 255, 100, 100);

        var borderColor = canPlace
            ? Color.FromArgb(200, 100, 255, 100)
            : Color.FromArgb(200, 255, 100, 100);

        var previewRect = new Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(fillColor), previewRect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), 2), previewRect);

        var nameText = new FormattedText(
            _interactionState.PlacementPreviewTemplate.Name,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            new SolidColorBrush(borderColor));

        context.DrawText(nameText, new Point(x + 5, y + 5));
    }

    private void DrawDragPreview(DrawingContext context)
    {
        if (_interactionState.DraggingComponent == null) return;

        double width = _interactionState.DraggingComponent.Width;
        double height = _interactionState.DraggingComponent.Height;
        double x = _interactionState.DragPreviewPosition.X;
        double y = _interactionState.DragPreviewPosition.Y;

        var fillColor = _interactionState.DragPreviewValid
            ? Color.FromArgb(50, 100, 255, 100)
            : Color.FromArgb(50, 255, 100, 100);

        var borderColor = _interactionState.DragPreviewValid
            ? Color.FromArgb(200, 100, 255, 100)
            : Color.FromArgb(200, 255, 100, 100);

        var previewRect = new Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(fillColor), previewRect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), 2), previewRect);
    }

    private void DrawConnectionPreview(DrawingContext context)
    {
        if (_interactionState.ConnectionDragStartPin == null) return;

        var (startX, startY) = _interactionState.ConnectionDragStartPin.GetAbsolutePosition();

        var targetPin = ViewModel?.HighlightedPin?.Pin;
        bool isValidTarget = targetPin != null && targetPin != _interactionState.ConnectionDragStartPin &&
                             targetPin.ParentComponent != _interactionState.ConnectionDragStartPin.ParentComponent;

        var previewPen = isValidTarget
            ? new Pen(Brushes.LimeGreen, 2)
            : new Pen(Brushes.Gray, 2) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };

        var endPoint = isValidTarget
            ? new Point(targetPin!.GetAbsolutePosition().x, targetPin.GetAbsolutePosition().y)
            : _interactionState.ConnectionDragCurrentPoint;

        context.DrawLine(previewPen, new Point(startX, startY), endPoint);

        context.DrawEllipse(Brushes.LimeGreen, null, new Point(startX, startY), 5, 5);

        if (isValidTarget)
        {
            context.DrawEllipse(Brushes.LimeGreen, null, endPoint, 5, 5);
        }
    }

    private void DrawSelectionRectangle(DrawingContext context, SelectionManager selection)
    {
        var (minX, minY, maxX, maxY) = selection.GetNormalizedBox();
        double width = maxX - minX;
        double height = maxY - minY;
        if (width < 1 && height < 1) return;

        var rect = new Rect(minX, minY, width, height);

        var fillBrush = new SolidColorBrush(Color.FromArgb(30, 100, 150, 255));
        context.FillRectangle(fillBrush, rect);

        var borderPen = new Pen(
            new SolidColorBrush(Color.FromArgb(180, 100, 180, 255)), 1.5)
        {
            DashStyle = new DashStyle(new double[] { 6, 3 }, 0)
        };
        context.DrawRectangle(null, borderPen, rect);
    }

    private void DrawModeIndicator(DrawingContext context, Rect bounds)
    {
        var mainVm = MainViewModel;
        if (mainVm == null) return;

        string modeText = mainVm.CurrentMode switch
        {
            InteractionMode.Select => "[S] Select",
            InteractionMode.PlaceComponent => "[P] Place",
            InteractionMode.Connect => "[C] Connect",
            InteractionMode.Delete => "[D] Delete",
            _ => ""
        };

        var brush = mainVm.CurrentMode switch
        {
            InteractionMode.Select => Brushes.LightBlue,
            InteractionMode.PlaceComponent => Brushes.LightGreen,
            InteractionMode.Connect => Brushes.Orange,
            InteractionMode.Delete => Brushes.Red,
            _ => Brushes.White
        };

        var text = new FormattedText(
            modeText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial", FontStyle.Normal, FontWeight.Bold),
            14,
            brush);

        context.DrawText(text, new Point(bounds.Width - 100, 10));
    }

    private void DrawStatusInfo(DrawingContext context, Rect bounds)
    {
        var vm = ViewModel;
        if (vm == null) return;

        string snapInfo = vm.GridSnap.IsEnabled
            ? $" | [G] Snap: {vm.GridSnap.GridSizeMicrometers}µm"
            : " | [G] Snap: OFF";
        string gridInfo = vm.ShowGridOverlay ? " | [Shift+G] Grid: ON" : "";
        string powerInfo = vm.ShowPowerFlow ? " | [P] Power: ON" : " | [P] Power: OFF";

        var statusText = new FormattedText(
            $"Zoom: {Zoom:P0} | Components: {vm.Components.Count} | Connections: {vm.Connections.Count}{snapInfo}{gridInfo}{powerInfo}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            Brushes.White);

        context.DrawText(statusText, new Point(10, bounds.Height - 25));
    }
}
