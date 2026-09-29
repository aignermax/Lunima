using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Laser line-shape explainer (#1152): two sentences plus an animated mini spectrum.
/// Opened from the "?" button next to the line-shape combo in the light-source
/// properties panel.
/// </summary>
public partial class LineShapeHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LineShapeHelpFlyout()
    {
        InitializeComponent();
    }
}
