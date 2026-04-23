using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for grid-snapping and pin-alignment guide preferences.
/// Wraps the canvas-owned <see cref="GridSnapSettings"/> and
/// <see cref="AlignmentGuideViewModel"/> so changes are live.
/// </summary>
public class GridSnapSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "Grid &amp; Alignment";

    /// <inheritdoc/>
    public string Icon => "⊞";

    /// <inheritdoc/>
    public string? Category => "Canvas";

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="GridSnapSettingsPage"/>.
    /// </summary>
    public GridSnapSettingsPage(DesignCanvasViewModel canvas)
    {
        ViewModel = new GridSnapSettingsViewModel(canvas.GridSnap, canvas.AlignmentGuide);
    }
}
