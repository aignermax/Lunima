using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Help of the PDK management section in the left panel (#1229): short sections
/// (#1152 text budget) plus a looping "a process belongs to its chiplet" diagram —
/// a process-A component seats on chiplet A, a process-B component is rejected
/// there and seats on chiplet B; seated components let a light pulse flow through
/// the chiplet's waveguide. Opened from the <c>HelpFlyoutButton</c> next to the
/// "PDK management" header.
/// </summary>
public partial class PdkHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public PdkHelpFlyout()
    {
        InitializeComponent();
    }
}
