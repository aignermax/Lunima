using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for design validation checks — runs rules and navigates to issues.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class DesignChecksPanel : UserControl
{
    /// <summary>Initializes the DesignChecksPanel.</summary>
    public DesignChecksPanel()
    {
        InitializeComponent();
    }
}
