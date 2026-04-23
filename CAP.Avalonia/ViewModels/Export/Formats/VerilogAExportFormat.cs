using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for Verilog-A / SPICE co-simulation netlists.
/// Opens an options dialog (output directory, circuit name, wavelength) before exporting.
/// </summary>
public class VerilogAExportFormat : IExportFormat
{
    private readonly VerilogAExportViewModel _vm;
    private readonly AsyncRelayCommand _exportCommand;

    /// <inheritdoc/>
    public string Name => "Verilog-A / SPICE";

    /// <inheritdoc/>
    public string Icon => "📊";

    /// <inheritdoc/>
    public string Description => "Export for co-simulation with electronic circuits (ngspice, Xyce)";

    /// <inheritdoc/>
    public string Background => "#4d3d5d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand => _exportCommand;

    /// <summary>
    /// Callback that opens the Verilog-A export options dialog.
    /// Must be set from the UI layer (e.g., MainWindow.axaml.cs) before the command is invoked.
    /// </summary>
    public Func<Task>? ShowOptionsDialogAsync { get; set; }

    /// <summary>
    /// The export options ViewModel — used by the dialog to show/edit settings.
    /// </summary>
    public VerilogAExportViewModel OptionsViewModel => _vm;

    /// <summary>Initializes with the Verilog-A export ViewModel.</summary>
    /// <param name="vm">Provides export settings and the core export command.</param>
    public VerilogAExportFormat(VerilogAExportViewModel vm)
    {
        _vm = vm;
        _exportCommand = new AsyncRelayCommand(RunExportFlowAsync);
    }

    private async Task RunExportFlowAsync()
    {
        if (ShowOptionsDialogAsync != null)
            await ShowOptionsDialogAsync();
        else
            await _vm.ExportCommand.ExecuteAsync(null);
    }
}
