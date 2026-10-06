using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Main help of the Logic tab (#1196): short sections (#1152 text budget) plus a
/// looping "light → 0/1" threshold diagram. Opened from the panel's
/// <c>HelpFlyoutButton</c> next to the Logic title.
/// </summary>
public partial class LogicHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LogicHelpFlyout()
    {
        InitializeComponent();
    }
}
