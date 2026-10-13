using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Controls.Canvas.CrossingDrag;

/// <summary>
/// Live state of an axis-constrained crossing drag, read by
/// <see cref="CrossingAxisDragOverlayRenderer"/>: where the crossing started, which axis the
/// drag locked onto (none until the pointer clearly left the press point), and the offset
/// along it. The crossing itself stays put until release; the overlay shows the guide line
/// and a ghost of the crossing at its target.
/// </summary>
public sealed class CrossingAxisDragPreview
{
    /// <summary>Creates the preview for a drag that starts on <paramref name="crossing"/>.</summary>
    public CrossingAxisDragPreview(ComponentViewModel crossing)
    {
        Crossing = crossing;
        CenterX = crossing.X + crossing.Width / 2.0;
        CenterY = crossing.Y + crossing.Height / 2.0;
    }

    /// <summary>The crossing being dragged.</summary>
    public ComponentViewModel Crossing { get; }

    /// <summary>Crossing centre X (µm) when the drag began.</summary>
    public double CenterX { get; }

    /// <summary>Crossing centre Y (µm) when the drag began.</summary>
    public double CenterY { get; }

    /// <summary>The axis the drag follows; null while the pointer is still near the press point.</summary>
    public CrossingAxis? Axis { get; set; }

    /// <summary>Signed offset (µm) along <see cref="Axis"/>.</summary>
    public double OffsetMicrometers { get; set; }

    /// <summary>False when the crossing cannot go to the current target (the ghost turns red).</summary>
    public bool IsValid { get; set; } = true;
}
