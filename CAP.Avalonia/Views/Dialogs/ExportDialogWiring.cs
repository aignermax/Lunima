using Avalonia.Controls;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Wires the <c>ShowOptionsDialogAsync</c> callbacks for export formats that need a config dialog.
/// Each callback is wrapped in a try/catch so dialog construction or ShowDialog failures
/// surface to the user via <see cref="MainViewModel.StatusText"/> instead of being swallowed
/// by AsyncRelayCommand's exception capture.
/// </summary>
public static class ExportDialogWiring
{
    /// <summary>Wires all export-dialog callbacks on the given ViewModel.</summary>
    /// <param name="vm">MainViewModel exposing the format adapters.</param>
    /// <param name="owner">Owner window used as parent for the modal dialogs.</param>
    public static void Wire(MainViewModel vm, Window owner)
    {
        vm.PhotonTorchExportFormat.ShowOptionsDialogAsync = () =>
            ShowSafelyAsync(vm, owner, "PhotonTorch",
                () => new PhotonTorchExportDialog { DataContext = vm.FileOperations.PhotonTorchExport });

        vm.VerilogAExportFormat.ShowOptionsDialogAsync = () =>
            ShowSafelyAsync(vm, owner, "Verilog-A",
                () => new VerilogAExportDialog { DataContext = vm.VerilogAExportFormat.OptionsViewModel });
    }

    private static async Task ShowSafelyAsync(MainViewModel vm, Window owner, string formatName, Func<Window> dialogFactory)
    {
        try
        {
            await dialogFactory().ShowDialog(owner);
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Failed to open {formatName} export dialog: {ex.Message}";
        }
    }
}
