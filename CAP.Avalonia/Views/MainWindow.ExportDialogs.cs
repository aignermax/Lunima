using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views.Export;

namespace CAP.Avalonia.Views;

/// <summary>
/// Partial portion of <see cref="MainWindow"/> that wires up the per-export
/// configuration dialogs for Verilog-A and PhotonTorch exports (issue #509).
/// Split out of the main code-behind to keep each file under 500 lines.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Wires the <see cref="FileOperationsViewModel"/> dialog delegates so that
    /// clicking the toolbar export buttons opens a configuration dialog before
    /// the export runs.
    /// </summary>
    private void WireExportDialogs(MainViewModel vm)
    {
        vm.FileOperations.ShowVerilogAExportDialogAsync = async () =>
        {
            var dialog = new VerilogAExportDialog
            {
                DataContext = vm.FileOperations.VerilogAExport
            };
            return await dialog.ShowDialog<bool>(this);
        };

        vm.FileOperations.ShowPhotonTorchExportDialogAsync = async () =>
        {
            var dialog = new PhotonTorchExportDialog
            {
                DataContext = vm.FileOperations.PhotonTorchExport
            };
            return await dialog.ShowDialog<bool>(this);
        };
    }
}
