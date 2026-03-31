using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.Gestures;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Mouse and keyboard event handling for DesignCanvas.
/// Delegates all pointer events to registered gesture recognizers in priority order.
/// </summary>
public partial class DesignCanvas
{
    private List<IGestureRecognizer> _gestureRecognizers = [];
    private IGestureRecognizer? _activeGesture;

    internal void InitGestures()
    {
        _gestureRecognizers =
        [
            new PanGestureRecognizer(_interactionState, InvalidateVisual),
            new ConnectionGestureRecognizer(_interactionState, InvalidateVisual),
            new PlacementGestureRecognizer(_interactionState, InvalidateVisual),
            new ComponentDragGestureRecognizer(_interactionState, InvalidateVisual, () => Zoom, c => Cursor = c),
            new SelectionBoxGestureRecognizer(_interactionState, InvalidateVisual),
        ];
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetPosition(this);
        _interactionState.LastPointerPosition = point;

        var vm = ViewModel;
        if (vm == null) return;

        var canvasPoint = ScreenToCanvas(point);

        _activeGesture = null;
        foreach (var recognizer in _gestureRecognizers)
        {
            if (recognizer.TryRecognize(e, canvasPoint, vm, MainViewModel))
            {
                _activeGesture = recognizer;
                break;
            }
        }

        e.Handled = true;
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);
        var delta = point - _interactionState.LastPointerPosition;
        var vm = ViewModel;
        if (vm == null) return;

        var canvasPoint = ScreenToCanvas(point);
        _interactionState.LastCanvasPosition = canvasPoint;

        foreach (var recognizer in _gestureRecognizers)
            recognizer.UpdatePassiveState(canvasPoint, vm, MainViewModel);

        _activeGesture?.OnPointerMoved(e, delta, canvasPoint, vm, MainViewModel);

        _interactionState.LastPointerPosition = point;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // Suppress context menu after a right-click pan
        if (_interactionState.HasPanned && e.InitialPressMouseButton == MouseButton.Right)
        {
            e.Handled = true;
            _interactionState.HasPanned = false;
            _interactionState.IsPanning = false;
            _activeGesture = null;
            return;
        }

        base.OnPointerReleased(e);

        var vm = ViewModel;
        if (vm != null)
            _activeGesture?.OnPointerReleased(e, vm, MainViewModel);

        _activeGesture = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y > 0 ? 1.1 : 0.9;
        var newZoom = Math.Clamp(Zoom * delta, 0.1, 10.0);
        var point = e.GetPosition(this);
        var vm = ViewModel;

        if (vm != null)
        {
            var beforeZoom = ScreenToCanvas(point);
            Zoom = newZoom;
            var afterZoom = ScreenToCanvas(point);
            vm.PanX += (afterZoom.X - beforeZoom.X) * Zoom;
            vm.PanY += (afterZoom.Y - beforeZoom.Y) * Zoom;
        }
        else
        {
            Zoom = newZoom;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private Point ScreenToCanvas(Point screenPoint)
    {
        var vm = ViewModel;
        if (vm == null) return screenPoint;
        return new Point(
            (screenPoint.X - vm.PanX) / Zoom,
            (screenPoint.Y - vm.PanY) / Zoom);
    }
}
