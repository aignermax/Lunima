using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Plain-language explainer for the Monte Carlo tab (#1344): why fabrication
/// variance makes the same design behave differently on every chip, and how to
/// read the histogram and yield — with a looping scatter animation. Opened from
/// the panel's "?" button.
/// </summary>
public partial class MonteCarloHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public MonteCarloHelpFlyout()
    {
        InitializeComponent();
    }
}
