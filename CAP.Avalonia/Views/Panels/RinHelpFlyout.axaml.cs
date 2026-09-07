using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// RIN (relative intensity noise) explainer (#1152): two sentences plus an animated
/// jittery power-over-time trace. Opened from the "?" button next to the RIN slider
/// in the light-source properties panel.
/// </summary>
public partial class RinHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public RinHelpFlyout()
    {
        InitializeComponent();
    }
}
