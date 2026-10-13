using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Help content of the "?" beside the toolbar's workspace switch: what each of the four
/// workspaces (Layout, Optics, Logic, System) is for and what Run does in it.
/// </summary>
public partial class WorkspaceHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public WorkspaceHelpFlyout()
    {
        InitializeComponent();
    }
}