using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Timeline help of the Logic tab (#1206): short sections (#1152 text budget) plus a
/// looping pulse-arrival diagram. Opened from the <c>HelpFlyoutButton</c> next to the
/// Timeline section header.
/// </summary>
public partial class LogicTimelineHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LogicTimelineHelpFlyout()
    {
        InitializeComponent();
    }
}
