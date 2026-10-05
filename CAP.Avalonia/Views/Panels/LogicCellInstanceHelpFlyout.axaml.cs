using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Cell-instance help of the Logic tab (#1411): three short sentences (#1152 text
/// budget) plus a looping "one template, stamped twice, independent state" diagram.
/// Opened from the <c>HelpFlyoutButton</c> next to the Outputs label, visible only
/// when the built network contains at least one cell group.
/// </summary>
public partial class LogicCellInstanceHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LogicCellInstanceHelpFlyout()
    {
        InitializeComponent();
    }
}
