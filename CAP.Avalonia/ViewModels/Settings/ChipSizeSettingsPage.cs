using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for the PIC chip footprint — preset selection, custom width/height
/// in millimeters, and live tile-grid count. Apply resizes the canvas boundary and
/// triggers a repaint. Lives in the Settings window because chip dimensions are a
/// design-wide configuration, not a per-action knob.
/// </summary>
public class ChipSizeSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "Chip Size";

    /// <inheritdoc/>
    public string Icon => "📏";

    /// <inheritdoc/>
    public string? Category => "Canvas";

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>Initializes a new instance of <see cref="ChipSizeSettingsPage"/>.</summary>
    public ChipSizeSettingsPage(ChipSizeViewModel chipSizeViewModel)
    {
        ViewModel = chipSizeViewModel;
    }
}
