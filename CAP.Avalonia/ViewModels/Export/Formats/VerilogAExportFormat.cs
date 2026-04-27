using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for Verilog-A / SPICE co-simulation netlists.
/// Opens an options dialog (wavelength, test bench) before exporting; the file
/// path is then chosen via a Save-File-Dialog inside the export flow.
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
    /// Must be set from the UI layer (see <c>Views.Dialogs.ExportDialogWiring.Wire</c>)
    /// before the command is invoked. Invoking the command before this is wired
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public Func<Task>? ShowOptionsDialogAsync { get; set; }

    /// <summary>
    /// The export options ViewModel — exposed so <c>Views.Dialogs.ExportDialogWiring.Wire</c>
    /// can inject it as the dialog DataContext. Encapsulation is intentionally relaxed
    /// here because the dialog needs a reference to the underlying VM.
    /// </summary>
    public VerilogAExportViewModel OptionsViewModel => _vm;

    /// <summary>Initializes with the Verilog-A export ViewModel.</summary>
    /// <param name="vm">Provides export settings and the core export command, used by the dialog DataContext.</param>
    public VerilogAExportFormat(VerilogAExportViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _exportCommand = new AsyncRelayCommand(RunExportFlowAsync);
    }

    private async Task RunExportFlowAsync()
    {
        if (ShowOptionsDialogAsync == null)
            throw new InvalidOperationException(
                $"{nameof(VerilogAExportFormat)}.{nameof(ShowOptionsDialogAsync)} has not been wired. " +
                "The UI layer (Views.Dialogs.ExportDialogWiring.Wire) must set this callback before the export command can run.");
        await ShowOptionsDialogAsync();
    }
}
