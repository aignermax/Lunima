using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Helpers;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Controls.Canvas.CrossingDrag;

/// <summary>
/// Snapping for an axis-constrained crossing drag, restricted to the drag's free coordinate:
/// a crossing port snaps onto a facing pin's X (horizontal drag) or Y (vertical drag) when it
/// comes within the alignment guide's snap tolerance — the same pin-to-pin rule as the
/// ordinary component drag — and otherwise the crossing centre snaps to the grid when grid
/// snap is on. The pin snap wins over the grid, because a crossing exists to line its wires
/// up with pins. The matching alignment guides are published on
/// <see cref="AlignmentGuideViewModel"/> so the usual guide lines show for the ghost.
/// </summary>
public sealed class CrossingAxisSnapper
{
    /// <summary>Tolerance (µm) for listing guides at an already snapped target.</summary>
    private const double SnappedGuideToleranceMicrometers = 1e-3;

    private readonly PinAlignmentHelper _helper = new();

    /// <summary>
    /// Returns the snapped offset for a raw drag of <paramref name="rawOffset"/> µm along
    /// <paramref name="axis"/> and updates the canvas alignment guides for the target.
    /// </summary>
    public double Snap(DesignCanvasViewModel canvas, CrossingAxisDragPreview drag, CrossingAxis axis, double rawOffset)
    {
        var guide = canvas.AlignmentGuide;
        double offset = guide.IsEnabled && guide.SnapEnabled
                        && TryPinSnap(canvas, drag, axis, rawOffset, guide.SnapToleranceMicrometers, out double snapped)
            ? snapped
            : GridSnap(canvas, drag, axis, rawOffset);
        ShowGuides(canvas, drag, axis, offset);
        return offset;
    }

    private bool TryPinSnap(DesignCanvasViewModel canvas, CrossingAxisDragPreview drag, CrossingAxis axis,
                            double rawOffset, double tolerance, out double snapped)
    {
        snapped = rawOffset;
        var free = FreeCoordinateAlignments(canvas, drag, axis, rawOffset, tolerance);
        if (free.Count == 0) return false;
        snapped = rawOffset + free.MinBy(Math.Abs);
        return true;
    }

    /// <summary>Signed distances (µm) from each crossing port to the facing pins it aligns with, along the free coordinate.</summary>
    private List<double> FreeCoordinateAlignments(DesignCanvasViewModel canvas, CrossingAxisDragPreview drag,
                                                  CrossingAxis axis, double offset, double tolerance)
    {
        _helper.AlignmentToleranceMicrometers = tolerance;
        var (dx, dy) = CrossingAxisMovePlanner.DeltaFor(axis, offset);
        var (horizontal, vertical) = _helper.FindAllAlignments(drag.Crossing.Component, Others(canvas, drag), dx, dy);
        return axis == CrossingAxis.Horizontal
            ? vertical.Select(a => a.XCoordinate - (a.DraggingPin.GetAbsolutePosition().x + dx)).ToList()
            : horizontal.Select(a => a.YCoordinate - (a.DraggingPin.GetAbsolutePosition().y + dy)).ToList();
    }

    private void ShowGuides(DesignCanvasViewModel canvas, CrossingAxisDragPreview drag, CrossingAxis axis, double offset)
    {
        var guide = canvas.AlignmentGuide;
        if (!guide.IsEnabled)
        {
            guide.ClearAlignments();
            return;
        }
        _helper.AlignmentToleranceMicrometers = SnappedGuideToleranceMicrometers;
        var (dx, dy) = CrossingAxisMovePlanner.DeltaFor(axis, offset);
        var (horizontal, vertical) = _helper.FindAllAlignments(drag.Crossing.Component, Others(canvas, drag), dx, dy);
        guide.HorizontalAlignments = axis == CrossingAxis.Vertical ? horizontal : new List<HorizontalAlignment>();
        guide.VerticalAlignments = axis == CrossingAxis.Horizontal ? vertical : new List<VerticalAlignment>();
    }

    /// <summary>Snaps the moved crossing centre to the grid along the axis when grid snap is on.</summary>
    private static double GridSnap(DesignCanvasViewModel canvas, CrossingAxisDragPreview drag, CrossingAxis axis, double offset)
    {
        if (canvas.GridSnap?.IsEnabled != true) return offset;
        bool horizontal = axis == CrossingAxis.Horizontal;
        var (sx, sy) = canvas.GridSnap.Snap(drag.CenterX + (horizontal ? offset : 0), drag.CenterY + (horizontal ? 0 : offset));
        return horizontal ? sx - drag.CenterX : sy - drag.CenterY;
    }

    private static IEnumerable<CAP_Core.Components.Core.Component> Others(DesignCanvasViewModel canvas, CrossingAxisDragPreview drag) =>
        canvas.Components.Where(c => c != drag.Crossing).Select(c => c.Component);
}
