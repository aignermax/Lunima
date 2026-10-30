using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.Gestures;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls.Canvas.LengthLock;

/// <summary>
/// A click on a route's length-lock padlock opens the lock menu next to it, instead of selecting
/// or dragging whatever lies underneath. Nothing else is handled here.
/// </summary>
internal sealed class LengthLockGestureRecognizer : IGestureRecognizer
{
    private readonly Func<double> _zoom;
    private readonly Action<WaveguideConnectionViewModel, MainViewModel> _openMenu;

    /// <param name="zoom">The canvas zoom (the padlock has a fixed size on screen).</param>
    /// <param name="openMenu">Opens the lock menu for a route.</param>
    public LengthLockGestureRecognizer(Func<double> zoom, Action<WaveguideConnectionViewModel, MainViewModel> openMenu)
    {
        _zoom = zoom;
        _openMenu = openMenu;
    }

    /// <inheritdoc/>
    public bool TryRecognize(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        if (mainVm == null || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return false;
        var connection = LengthLockHitTest.Hit(canvasPoint, canvas, _zoom());
        if (connection == null)
            return false;
        _openMenu(connection, mainVm);
        e.Handled = true;
        return true;
    }

    /// <inheritdoc/>
    public void UpdatePassiveState(Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm) { }

    /// <inheritdoc/>
    public void OnPointerMoved(PointerEventArgs e, Point delta, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm) { }

    /// <inheritdoc/>
    public void OnPointerReleased(PointerReleasedEventArgs e, DesignCanvasViewModel canvas, MainViewModel? mainVm) { }

    /// <inheritdoc/>
    public void Cancel() { }
}
