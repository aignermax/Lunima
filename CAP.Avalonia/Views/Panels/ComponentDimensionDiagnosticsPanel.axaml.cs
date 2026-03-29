using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for component dimension diagnostics — validates that GDS export dimensions match the UI display.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class ComponentDimensionDiagnosticsPanel : UserControl
{
    /// <summary>Initializes the ComponentDimensionDiagnosticsPanel.</summary>
    public ComponentDimensionDiagnosticsPanel()
    {
        InitializeComponent();
    }
}
