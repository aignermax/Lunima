using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Plain-language explainer for the process-management window: what a fabrication
/// process is, what layers / cross-sections / materials mean, and why electrical
/// routing needs its own metal cross-section — with the looping layer-stack
/// animation. Opened from the window's <c>HelpFlyoutButton</c> (#1152).
/// </summary>
public partial class ProcessHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public ProcessHelpFlyout()
    {
        InitializeComponent();
    }
}
