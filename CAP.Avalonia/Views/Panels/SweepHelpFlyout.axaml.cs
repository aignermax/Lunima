using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Plain-language explainer for the Sweep tab (#1352): what a parameter sweep
/// re-simulates and how to read the resulting curve (sensitivity, cos² fringe,
/// quadrature bias point) — with a looping slider/fringe animation. Opened from
/// the panel's "?" button.
/// </summary>
public partial class SweepHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public SweepHelpFlyout()
    {
        InitializeComponent();
    }
}
