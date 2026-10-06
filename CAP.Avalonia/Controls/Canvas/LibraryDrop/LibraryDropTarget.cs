using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.Controls.Canvas.LibraryDrop;

/// <summary>
/// Drop-target half of library drag&amp;drop (issue #1157). While a library template is
/// dragged over the canvas, the placement ghost follows the pointer (grid-snapped); on
/// release the template is placed at that point and the canvas returns to Select mode.
/// Attached once by <see cref="DesignCanvas"/>.
/// </summary>
public sealed class LibraryDropTarget
{
    /// <summary>Drag-data format carrying a <see cref="ComponentTemplate"/>.</summary>
    public const string ComponentTemplateFormat = "Lunima.ComponentTemplate";

    /// <summary>Drag-data format carrying a <see cref="GroupTemplate"/>.</summary>
    public const string GroupTemplateFormat = "Lunima.GroupTemplate";

    private readonly DesignCanvas _canvas;
    private readonly Func<Point, Point> _screenToCanvas;

    private LibraryDropTarget(DesignCanvas canvas, Func<Point, Point> screenToCanvas)
    {
        _canvas = canvas;
        _screenToCanvas = screenToCanvas;
    }

    /// <summary>Enables dropping library templates onto <paramref name="canvas"/>.</summary>
    public static LibraryDropTarget Attach(DesignCanvas canvas, Func<Point, Point> screenToCanvas)
    {
        var target = new LibraryDropTarget(canvas, screenToCanvas);
        DragDrop.SetAllowDrop(canvas, true);
        canvas.AddHandler(DragDrop.DragOverEvent, target.OnDragOver);
        canvas.AddHandler(DragDrop.DragLeaveEvent, target.OnDragLeave);
        canvas.AddHandler(DragDrop.DropEvent, target.OnDrop);
        return target;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var template = e.Data.Get(ComponentTemplateFormat) as ComponentTemplate;
        var groupTemplate = e.Data.Get(GroupTemplateFormat) as GroupTemplate;
        if (_canvas.ViewModel == null || _canvas.MainViewModel == null
            || (template == null && groupTemplate == null))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;

        var (sx, sy) = SnapToGrid(e.GetPosition(_canvas));
        var state = _canvas.InteractionState;
        if (template != null)
        {
            state.ResetGroupTemplatePlacementPreview();
            state.ShowPlacementPreview = true;
            state.PlacementPreviewTemplate = template;
            state.PlacementPreviewPosition = new Point(sx, sy);
        }
        else
        {
            state.ResetPlacementPreview();
            state.ShowGroupTemplatePlacementPreview = true;
            state.GroupTemplatePlacementPreview = groupTemplate;
            state.GroupTemplatePlacementPreviewPosition = new Point(sx, sy);
        }

        var name = template?.Name ?? groupTemplate!.Name;
        _canvas.MainViewModel.CanvasInteraction.UpdateStatus?.Invoke(string.Format(
            LocalizationService.Instance.Translate("Status.DragDropPlace"), name));
        _canvas.InvalidateVisual();
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ClearPreviews();

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ClearPreviews();
        var mainVm = _canvas.MainViewModel;
        if (mainVm == null) return;

        var (sx, sy) = SnapToGrid(e.GetPosition(_canvas));
        if (e.Data.Get(ComponentTemplateFormat) is ComponentTemplate template)
        {
            mainVm.CanvasInteraction.DropComponentTemplateAt(template, sx, sy);
            e.Handled = true;
        }
        else if (e.Data.Get(GroupTemplateFormat) is GroupTemplate groupTemplate)
        {
            mainVm.CanvasInteraction.DropGroupTemplateAt(groupTemplate, sx, sy);
            e.Handled = true;
        }
    }

    private (double x, double y) SnapToGrid(Point screenPoint)
    {
        var canvasPoint = _screenToCanvas(screenPoint);
        var vm = _canvas.ViewModel!;
        return vm.GridSnap.Snap(canvasPoint.X, canvasPoint.Y);
    }

    private void ClearPreviews()
    {
        _canvas.InteractionState.ResetPlacementPreview();
        _canvas.InteractionState.ResetGroupTemplatePlacementPreview();
        _canvas.InvalidateVisual();
    }
}
