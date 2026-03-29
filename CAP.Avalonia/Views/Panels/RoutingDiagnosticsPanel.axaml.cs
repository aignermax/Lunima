using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for waveguide routing diagnostics — analyzes path-finding performance and issues.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class RoutingDiagnosticsPanel : UserControl
{
    /// <summary>Initializes the RoutingDiagnosticsPanel.</summary>
    public RoutingDiagnosticsPanel()
    {
        InitializeComponent();
    }
}
