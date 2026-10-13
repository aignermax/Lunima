using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels.WorkspaceRail;

/// <summary>
/// The left rail's Chiplets pane (System workspace default): the design's chiplets with
/// their process; a click selects the chiplet and centers the canvas on it.
/// </summary>
public partial class ChipletsPane : UserControl
{
    /// <summary>Initializes the pane.</summary>
    public ChipletsPane()
    {
        InitializeComponent();
    }
}
