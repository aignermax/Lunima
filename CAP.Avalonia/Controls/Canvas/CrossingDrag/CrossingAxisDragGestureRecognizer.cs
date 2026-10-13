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
/// wires: the dominant drag direction picks the axis, a guide line and a ghost show the
/// target, and the release commits one undoable <see cref="MoveCrossingAlongAxisCommand"/>.
/// Holding Shift when pressing frees the drag — the ordinary component drag then handles it
/// (Ctrl and Alt keep their selection meaning). Registered before the component drag.
/// </summary>
public sealed class CrossingAxisDragGestureRecognizer : IGestureRecognizer
{
    /// <summary>Pointer travel (screen px) before the drag locks onto an axis.</summary>
    private const double AxisLockThresholdPx = 4.0;

    private readonly CanvasInteractionState _state;
    private readonly Action _invalidate;
    private readonly Func<double> _getZoom;
    private Point _pressPoint;

    /// <summary>Initializes a new instance of <see cref="CrossingAxisDragGestureRecognizer"/>.</summary>
    public CrossingAxisDragGestureRecognizer(CanvasInteractionState state, Action invalidate, Func<double> getZoom)
    {
        _state = state;
        _invalidate = invalidate;
        _getZoom = getZoom;
    }

    /// <inheritdoc/>
    public bool TryRecognize(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        if (mainVm?.CanvasInteraction.CurrentMode != InteractionMode.Select) return false;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return false;
        if (e.KeyModifiers != KeyModifiers.None) return false;

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
        _state.CrossingAxisDrag = new CrossingAxisDragPreview(hit);
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
        var drag = _state.CrossingAxisDrag;
        if (drag == null) return;

        double dx = canvasPoint.X - _pressPoint.X, dy = canvasPoint.Y - _pressPoint.Y;
        double threshold = AxisLockThresholdPx / Zoom();
        if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold)
        {
            drag.Axis = null;
            drag.OffsetMicrometers = 0;
            drag.IsValid = true;
        }
        else
        {
            drag.Axis = Math.Abs(dx) >= Math.Abs(dy) ? CrossingAxis.Horizontal : CrossingAxis.Vertical;
            drag.OffsetMicrometers = SnapOffset(canvas, drag, drag.Axis == CrossingAxis.Horizontal ? dx : dy);
            drag.IsValid = MoveCrossingAlongAxisCommand.Validate(
                canvas, drag.Crossing.Component, drag.Axis.Value, drag.OffsetMicrometers) == null;
        }
        _invalidate();
    }

    /// <summary>Snaps the moved crossing centre to the grid along the axis when grid snap is on.</summary>
    private static double SnapOffset(DesignCanvasViewModel canvas, CrossingAxisDragPreview drag, double offset)
    {
        if (canvas.GridSnap?.IsEnabled != true) return offset;
        bool horizontal = drag.Axis == CrossingAxis.Horizontal;
        var (sx, sy) = canvas.GridSnap.Snap(drag.CenterX + (horizontal ? offset : 0), drag.CenterY + (horizontal ? 0 : offset));
        return horizontal ? sx - drag.CenterX : sy - drag.CenterY;
    }

    /// <inheritdoc/>
    public void OnPointerReleased(PointerReleasedEventArgs e, DesignCanvasViewModel canvas, MainViewModel? mainVm)
    {
        var drag = _state.CrossingAxisDrag;
        _state.CrossingAxisDrag = null;
        if (drag?.Axis is not { } axis || mainVm == null || Math.Abs(drag.OffsetMicrometers) < 1e-3)
        {
            _invalidate();
            return;
        }

        var command = new MoveCrossingAlongAxisCommand(
            canvas, drag.Crossing.Component, axis, drag.OffsetMicrometers, mainVm.ErrorConsole);
        if (MoveCrossingAlongAxisCommand.Validate(canvas, drag.Crossing.Component, axis, drag.OffsetMicrometers) != null)
        {
            mainVm.StatusText = LocalizationService.Instance.Translate("Status.CrossingMoveBlocked");
        }
        else
        {
            mainVm.CommandManager.ExecuteCommand(command);
            mainVm.StatusText = LocalizationService.Instance.Translate("Status.CrossingMoved");
        }
        _invalidate();
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        _state.CrossingAxisDrag = null;
        _invalidate();
    }

    private double Zoom()
    {
        double zoom = _getZoom();
        return zoom <= 0 ? 1.0 : zoom;
    }
}
