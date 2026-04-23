using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// ViewModel for the Grid Snap settings page.
/// Wraps the existing <see cref="GridSnapSettings"/> and
/// <see cref="AlignmentGuideViewModel"/> instances from the canvas,
/// so changes take effect immediately on the canvas without copying state.
/// </summary>
public class GridSnapSettingsViewModel
{
    /// <summary>Grid-snapping settings (enabled state and snap size).</summary>
    public GridSnapSettings GridSnap { get; }

    /// <summary>Pin-alignment guide settings (show guides, snap-to-align).</summary>
    public AlignmentGuideViewModel AlignmentGuide { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="GridSnapSettingsViewModel"/>.
    /// </summary>
    public GridSnapSettingsViewModel(GridSnapSettings gridSnap, AlignmentGuideViewModel alignmentGuide)
    {
        GridSnap = gridSnap;
        AlignmentGuide = alignmentGuide;
    }
}
