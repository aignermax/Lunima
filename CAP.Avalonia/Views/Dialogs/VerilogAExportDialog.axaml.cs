using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Dialog window showing Verilog-A / SPICE export options and triggering the export.
/// DataContext must be set to <see cref="CAP.Avalonia.ViewModels.Export.VerilogAExportViewModel"/>.
/// </summary>
public partial class VerilogAExportDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public VerilogAExportDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
