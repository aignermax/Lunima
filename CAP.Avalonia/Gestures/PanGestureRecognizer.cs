using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Gestures;

/// <summary>
/// Handles canvas panning via middle or right mouse button drag.
/// Sets IsPanning state and updates PanX/PanY on the canvas ViewModel.
/// </summary>
public class PanGestureRecognizer : IGestureRecognizer
{
    private readonly CanvasInteractionState _state;
    private readonly Action _invalidate;

    /// <summary>Initializes a new instance of <see cref="PanGestureRecognizer"/>.</summary>
    public PanGestureRecognizer(CanvasInteractionState state, Action invalidate)
    {
        _state = state;
        _invalidate = invalidate;
    }

    /// <inheritdoc/>
    public bool TryRecognize(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        var props = e.GetCurrentPoint(null).Properties;
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _state.IsPanning = true;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void UpdatePassiveState(Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        // No passive visual updates needed for panning
    }

    /// <inheritdoc/>
    public void OnPointerMoved(PointerEventArgs e, Point delta, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        if (!_state.IsPanning) return;

        canvas.PanX += delta.X;
        canvas.PanY += delta.Y;
        _state.HasPanned = true;
        _invalidate();
    }

    /// <inheritdoc/>
    public void OnPointerReleased(PointerReleasedEventArgs e, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        _state.IsPanning = false;
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        _state.IsPanning = false;
        _state.HasPanned = false;
    }
}
