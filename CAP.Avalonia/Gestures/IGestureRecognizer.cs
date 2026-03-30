using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Gestures;

/// <summary>
/// Contract for a gesture recognizer that handles a specific pointer interaction pattern.
/// Recognizers are tried in order; the first one to accept a pressed event becomes the active gesture.
/// </summary>
public interface IGestureRecognizer
{
    /// <summary>
    /// Called on pointer press. Returns true if this recognizer accepts and handles the event.
    /// </summary>
    bool TryRecognize(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm);

    /// <summary>
    /// Called on every pointer move for passive visual updates (hover state, previews).
    /// Always called regardless of which gesture is active.
    /// </summary>
    void UpdatePassiveState(Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm);

    /// <summary>
    /// Called on every pointer move when this recognizer is the active gesture.
    /// </summary>
    void OnPointerMoved(PointerEventArgs e, Point delta, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm);

    /// <summary>
    /// Called on pointer release when this recognizer is the active gesture.
    /// </summary>
    void OnPointerReleased(PointerReleasedEventArgs e, DesignCanvasViewModel canvas, MainViewModel? mainVm);

    /// <summary>
    /// Cancels the current gesture and resets internal state.
    /// </summary>
    void Cancel();
}
