using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels.WorkspaceRail;

/// <summary>
/// "Place circuit parts in Layout ›": the hint a workspace with a restricted tool set (Optics
/// instruments, Logic gates) shows in its left pane, switching to Layout in one click.
/// </summary>
public partial class PlaceInLayoutHint : UserControl
{
    /// <summary>Initializes the hint.</summary>
    public PlaceInLayoutHint()
    {
        InitializeComponent();
    }
}
