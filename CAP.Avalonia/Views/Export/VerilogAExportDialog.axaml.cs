using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Export;

/// <summary>
/// Modal dialog for per-export Verilog-A / SPICE configuration.
/// Shows circuit name, output directory, wavelength, and test bench options so
/// the user can review and adjust them before the export runs. Returns <c>true</c>
/// when the user confirms; <c>false</c> when they cancel.
/// </summary>
public partial class VerilogAExportDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public VerilogAExportDialog()
    {
        InitializeComponent();
    }

    private void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
