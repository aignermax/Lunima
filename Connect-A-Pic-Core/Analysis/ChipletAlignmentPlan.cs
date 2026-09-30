using CAP_Core.Components.Core;

namespace CAP_Core.Analysis;

/// <summary>
/// Reason a chiplet-link alignment was refused.
/// </summary>
public enum ChipletAlignmentRefusal
{
    /// <summary>The connection is not a cross-chiplet edge-coupler link.</summary>
    NotCrossChipletLink,

    /// <summary>The facets do not face each other; rotation is out of scope for the one-click fix.</summary>
    NotFacing,

    /// <summary>The link is already butt-coupled (zero offset, zero gap) — nothing to do.</summary>
    AlreadyAligned,

    /// <summary>The target position overlaps another component or chiplet.</summary>
    Overlap,

    /// <summary>The end chiplet is linked to a third chiplet whose alignment the move would break.</summary>
    WouldBreakOtherLink,
}

/// <summary>
/// Translation of the end pin's chiplet that snaps a cross-chiplet edge-coupler link
/// into perfect butt-coupling.
/// </summary>
/// <param name="Chiplet">The top-level group to move.</param>
/// <param name="DeltaX">Horizontal translation in micrometers.</param>
/// <param name="DeltaY">Vertical translation in micrometers.</param>
public sealed record ChipletAlignmentPlan(ComponentGroup Chiplet, double DeltaX, double DeltaY);
