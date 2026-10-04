using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Main help of the Truth Table panel (#1207): short sections (#1152 text budget)
/// plus a looping interference diagram — two input pulses and the bias arm meet in a
/// coupler; in phase they add (bright output, badge 1), in anti-phase they cancel
/// (dark output, badge 0). Opened from the panel's <c>HelpFlyoutButton</c> next to
/// the Truth Table title.
/// </summary>
public partial class TruthTableHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public TruthTableHelpFlyout()
    {
        InitializeComponent();
    }
}
