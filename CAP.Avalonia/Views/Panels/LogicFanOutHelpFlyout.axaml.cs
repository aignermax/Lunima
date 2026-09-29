using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Fan-out help of the Logic tab (#1216): short sections (#1152 text budget) plus a
/// looping power-splitting diagram. Opened from the <c>HelpFlyoutButton</c> inside the
/// fan-out warning banner.
/// </summary>
public partial class LogicFanOutHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LogicFanOutHelpFlyout()
    {
        InitializeComponent();
    }
}
