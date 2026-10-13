using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Gestures;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Controls.Canvas.CrossingDrag;

/// <summary>
/// Dragging a crossing with wires docked on it (Select mode) slides it along one of its two
/// wires: the dominant drag direction picks the axis, the target snaps to facing pins (or the
/// grid) along that axis, a guide line and a ghost show it, and the release commits one
/// undoable <see cref="MoveCrossingAlongAxisCommand"/>.
/// Shift frees the drag — held at the press or pressed at any point during the drag, the
/// gesture becomes an ordinary component move (full pin/grid snapping, the docked pieces are
/// re-routed on release) and stays free until release. The free move is delegated to the
/// <see cref="ComponentDragGestureRecognizer"/>; Ctrl and Alt keep their selection meaning.
/// Registered before the component drag.
/// </summary>
public sealed class CrossingAxisDragGestureRecognizer : IGestureRecognizer
{
    /// <summary>Pointer travel (screen px) before the drag locks onto an axis.</summary>
    private const double AxisLockThresholdPx = 4.0;

    private readonly CanvasInteractionState _state;
    private readonly Action _invalidate;
    private readonly Func<double> _getZoom;
    private readonly ComponentDragGestureRecognizer _freeDrag;
    private readonly CrossingAxisSnapper _snapper = new();
    private Point _pressPoint;
    private bool _isFree;
    private DesignCanvasViewModel? _canvas;
    private MainViewModel? _mainVm;

    /// <summary>Initializes a new instance of <see cref="CrossingAxisDragGestureRecognizer"/>.</summary>
    /// <param name="state">Shared canvas interaction state.</param>
    /// <param name="invalidate">Repaints the canvas.</param>
    /// <param name="getZoom">Current canvas zoom.</param>
    /// <param name="freeDrag">The ordinary component drag that takes over when Shift frees the drag.</param>
    public CrossingAxisDragGestureRecognizer(CanvasInteractionState state, Action invalidate, Func<double> getZoom,
                                             ComponentDragGestureRecognizer freeDrag)
    {
        _state = state;
        _invalidate = invalidate;
        _getZoom = getZoom;
        _freeDrag = freeDrag;
    }

    /// <inheritdoc/>
    public bool TryRecognize(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        if (mainVm?.CanvasInteraction.CurrentMode != InteractionMode.Select) return false;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return false;
        if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None) return false;

        var hit = DesignCanvasHitTesting.HitTestComponent(canvasPoint, canvas);
        if (hit == null || !IsDraggableCrossing(hit, canvas)) return false;
        // A crossing inside a multi-selection moves with the selection (group drag).
        bool selected = canvas.Selection.SelectedComponents.Contains(hit);
        if (selected && canvas.Selection.SelectedComponents.Count > 1) return false;

        if (!selected)
        {
            canvas.Selection.SelectSingle(hit);
            canvas.SelectedComponent = hit;
        }
        mainVm.CanvasClicked(canvasPoint.X, canvasPoint.Y);
        _pressPoint = canvasPoint;
        _isFree = false;
        _canvas = canvas;
        _mainVm = mainVm;
        _state.CrossingAxisDrag = new CrossingAxisDragPreview(hit);
        ShowHint(mainVm, "Status.CrossingDragHint");
        _invalidate();
        return true;
    }

    /// <summary>A top-level (or group-edit) crossing that is not locked and has a wire docked on it.</summary>
    private static bool IsDraggableCrossing(ComponentViewModel hit, DesignCanvasViewModel canvas)
    {
        var crossing = hit.Component;
        if (!CrossingComponentCatalog.IsCrossing(crossing) || crossing.IsLocked) return false;
        if (crossing.ParentGroup != null && !canvas.IsInGroupEditMode) return false;
        if (!canvas.Components.Contains(hit)) return false;
        lock (canvas.ConnectionManager.SyncRoot)
            return CrossingAxisMovePlanner.DockedPieces(crossing, canvas.ConnectionManager.Connections).Count > 0;
    }

    /// <inheritdoc/>
    public void UpdatePassiveState(Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
    }

    /// <inheritdoc/>
    public void OnPointerMoved(PointerEventArgs e, Point delta, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        if (_isFree)
        {
            _freeDrag.OnPointerMoved(e, delta, canvasPoint, canvas, mainVm);
            return;
        }
        var drag = _state.CrossingAxisDrag;
        if (drag == null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SwitchToFreeMove(e, drag, canvasPoint, canvas, mainVm);
            return;
        }
        UpdateAxisPreview(drag, canvasPoint, canvas);
        _invalidate();
    }

    private void UpdateAxisPreview(CrossingAxisDragPreview drag, Point canvasPoint, DesignCanvasViewModel canvas)
    {
        double dx = canvasPoint.X - _pressPoint.X, dy = canvasPoint.Y - _pressPoint.Y;
        double threshold = AxisLockThresholdPx / Zoom();
        if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold)
        {
            drag.Axis = null;
            drag.OffsetMicrometers = 0;
            drag.IsValid = true;
            canvas.AlignmentGuide.ClearAlignments();
            return;
        }
        var axis = Math.Abs(dx) >= Math.Abs(dy) ? CrossingAxis.Horizontal : CrossingAxis.Vertical;
        drag.Axis = axis;
        drag.OffsetMicrometers = _snapper.Snap(canvas, drag, axis, axis == CrossingAxis.Horizontal ? dx : dy);
        drag.IsValid = MoveCrossingAlongAxisCommand.Validate(
            canvas, drag.Crossing.Component, axis, drag.OffsetMicrometers) == null;
    }

    /// <summary>
    /// Hands the gesture to the ordinary component drag: the crossing starts a normal move and
    /// jumps to where the pointer has travelled since the press.
    /// </summary>
    private void SwitchToFreeMove(PointerEventArgs e, CrossingAxisDragPreview drag, Point canvasPoint,
                                  DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        _state.CrossingAxisDrag = null;
        canvas.AlignmentGuide.ClearAlignments();
        _isFree = true;
        _freeDrag.BeginMove(drag.Crossing, canvas, mainVm);
        double zoom = Zoom();
        var travelled = new Point((canvasPoint.X - _pressPoint.X) * zoom, (canvasPoint.Y - _pressPoint.Y) * zoom);
        _freeDrag.OnPointerMoved(e, travelled, canvasPoint, canvas, mainVm);
        ShowHint(mainVm, "Status.CrossingFreeMove");
    }

    /// <inheritdoc/>
    public void OnPointerReleased(PointerReleasedEventArgs e, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        mainVm?.CanvasInteraction.RefreshModeShortcutHints();
        if (_isFree)
        {
            _isFree = false;
            _freeDrag.OnPointerReleased(e, canvas, mainVm);
            return;
        }
        var drag = _state.CrossingAxisDrag;
        _state.CrossingAxisDrag = null;
        canvas.AlignmentGuide.ClearAlignments();
        if (drag?.Axis is not { } axis || mainVm == null || Math.Abs(drag.OffsetMicrometers) < 1e-3)
        {
            _invalidate();
            return;
        }
        CommitAxisMove(drag, axis, canvas, mainVm);
        _invalidate();
    }

    private static void CommitAxisMove(CrossingAxisDragPreview drag, CrossingAxis axis, DesignCanvasViewModel canvas, MainViewModel mainVm)
    {
        if (MoveCrossingAlongAxisCommand.Validate(canvas, drag.Crossing.Component, axis, drag.OffsetMicrometers) != null)
        {
            mainVm.StatusText = LocalizationService.Instance.Translate("Status.CrossingMoveBlocked");
            return;
        }
        mainVm.CommandManager.ExecuteCommand(new MoveCrossingAlongAxisCommand(
            canvas, drag.Crossing.Component, axis, drag.OffsetMicrometers, mainVm.ErrorConsole));
        mainVm.StatusText = LocalizationService.Instance.Translate("Status.CrossingMoved");
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        if (_isFree)
        {
            _isFree = false;
            _freeDrag.Cancel();
        }
        _state.CrossingAxisDrag = null;
        _canvas?.AlignmentGuide.ClearAlignments();
        _mainVm?.CanvasInteraction.RefreshModeShortcutHints();
        _invalidate();
    }

    /// <summary>Puts the crossing-drag modifier hint in the status line and the status-bar shortcut hint.</summary>
    private static void ShowHint(MainViewModel? mainVm, string statusKey)
    {
        if (mainVm == null) return;
        mainVm.StatusText = LocalizationService.Instance.Translate(statusKey);
        mainVm.CanvasInteraction.ModeShortcutHints = LocalizationService.Instance.Translate("StatusBar.Hints.CrossingDrag");
    }

    private double Zoom()
    {
        double zoom = _getZoom();
        return zoom <= 0 ? 1.0 : zoom;
    }
}
