using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Plain-language explainer for the Optimization tab (#1362): how the seeded
/// hill-climb tunes the sliders, what Budget and Seed mean, and why the result is a
/// local best — with a looping hill-climb animation. Opened from the panel's "?"
/// button.
/// </summary>
public partial class OptimizationHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public OptimizationHelpFlyout()
    {
        InitializeComponent();
    }
}
