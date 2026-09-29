using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Timing help of the Logic tab (#1206): short sections (#1152 text budget) on
/// propagation delay and the critical path. Opened from the <c>HelpFlyoutButton</c>
/// next to the critical-path line.
/// </summary>
public partial class LogicTimingHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LogicTimingHelpFlyout()
    {
        InitializeComponent();
    }
}
