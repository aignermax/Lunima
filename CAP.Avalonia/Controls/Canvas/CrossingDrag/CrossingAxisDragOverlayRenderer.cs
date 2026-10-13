using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Controls.Canvas.CrossingDrag;

/// <summary>
/// Draws the feedback of an axis-constrained crossing drag in world coordinates: a subtle
/// dashed guide line through the crossing's start centre along the locked axis (both axes,
/// fainter, until the drag locks onto one) and a ghost outline of the crossing at its target —
/// accent when the move is possible, danger red when it would overlap. Sizes are divided by
/// the zoom so they stay screen-constant.
/// </summary>
public sealed class CrossingAxisDragOverlayRenderer : ICanvasRenderer
{
    private const double GuideWidthPx = 1.0;
    private const double GuideDashPx = 5.0;
    private const double GhostStrokePx = 1.5;
    private const byte LockedGuideAlpha = 150;
    private const byte CandidateGuideAlpha = 70;
    private const byte GhostFillAlpha = 40;

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        var drag = rc.InteractionState.CrossingAxisDrag;
        if (drag == null) return;

        double zoom = rc.Zoom <= 0 ? 1.0 : rc.Zoom;
        var viewport = new Rect(-rc.ViewModel.PanX / zoom, -rc.ViewModel.PanY / zoom,
                                rc.Bounds.Width / zoom, rc.Bounds.Height / zoom);
        if (drag.Axis is { } axis)
        {
            DrawGuide(context, drag, axis, viewport, zoom, LockedGuideAlpha);
            DrawGhost(context, drag, axis, zoom);
            return;
        }
        DrawGuide(context, drag, CrossingAxis.Horizontal, viewport, zoom, CandidateGuideAlpha);
        DrawGuide(context, drag, CrossingAxis.Vertical, viewport, zoom, CandidateGuideAlpha);
    }

    private static void DrawGuide(DrawingContext context, CrossingAxisDragPreview drag, CrossingAxis axis,
                                  Rect viewport, double zoom, byte alpha)
    {
        var pen = new Pen(CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Accent, alpha)), GuideWidthPx / zoom)
        {
            DashStyle = new DashStyle(new[] { GuideDashPx, GuideDashPx }, 0),
        };
        if (axis == CrossingAxis.Horizontal)
            context.DrawLine(pen, new Point(viewport.Left, drag.CenterY), new Point(viewport.Right, drag.CenterY));
        else
            context.DrawLine(pen, new Point(drag.CenterX, viewport.Top), new Point(drag.CenterX, viewport.Bottom));
    }

    private static void DrawGhost(DrawingContext context, CrossingAxisDragPreview drag, CrossingAxis axis, double zoom)
    {
        var (dx, dy) = CrossingAxisMovePlanner.DeltaFor(axis, drag.OffsetMicrometers);
        var color = drag.IsValid ? CanvasPalette.Accent : CanvasPalette.Danger;
        var rect = new Rect(drag.Crossing.X + dx, drag.Crossing.Y + dy, drag.Crossing.Width, drag.Crossing.Height);
        context.DrawRectangle(CanvasPalette.Brush(CanvasPalette.WithAlpha(color, GhostFillAlpha)),
                              new Pen(CanvasPalette.Brush(color), GhostStrokePx / zoom), rect);
    }
}
