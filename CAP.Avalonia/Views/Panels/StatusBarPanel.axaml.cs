using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Status bar panel displaying current status text and keyboard shortcut hints.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class StatusBarPanel : UserControl
{
    /// <summary>Initializes the StatusBarPanel.</summary>
    public StatusBarPanel()
    {
        InitializeComponent();
    }
}
