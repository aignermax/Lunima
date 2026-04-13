using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for Verilog-A / SPICE export.
/// Allows exporting photonic circuits for mixed-signal co-simulation.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class VerilogAExportPanel : UserControl
{
    /// <summary>Initializes the VerilogAExportPanel.</summary>
    public VerilogAExportPanel()
    {
        InitializeComponent();
    }
}
