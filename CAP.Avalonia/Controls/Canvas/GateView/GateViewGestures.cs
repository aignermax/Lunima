using CAP.Avalonia.Gestures;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>
/// The canvas gestures that make sense at gate level: pan, place, select (click and
/// box), move whole gates and connect their terminals. Waveguide-level gestures — bend and
/// segment handles, the cut tool, crossing slides, frozen-path drags — stay with the
/// physical view, which the gate view does not draw.
/// </summary>
internal static class GateViewGestures
{
    /// <summary>True when the recognizer may run while the gate view is shown.</summary>
    public static bool Allows(IGestureRecognizer recognizer) => recognizer is
        GateConnectionGestureRecognizer
        or PanGestureRecognizer
        or PlacementGestureRecognizer
        or ComponentDragGestureRecognizer
        or SelectionBoxGestureRecognizer
        or HoverHighlightGestureRecognizer;
}
