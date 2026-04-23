using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for PhotonTorch GPU-simulation scripts.
/// Opens an options dialog so users can configure wavelength and simulation mode
/// before exporting.
/// </summary>
public class PhotonTorchExportFormat : IExportFormat
{
    private readonly PhotonTorchExportViewModel _vm;
    private readonly AsyncRelayCommand _exportCommand;

    /// <inheritdoc/>
    public string Name => "PhotonTorch";

    /// <inheritdoc/>
    public string Icon => "🔦";

    /// <inheritdoc/>
    public string Description => "Export to PhotonTorch for time-domain GPU-accelerated simulation";

    /// <inheritdoc/>
    public string Background => "#3d4d6d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand => _exportCommand;

    /// <summary>
    /// Callback that opens the PhotonTorch export options dialog.
    /// Must be set from the UI layer (e.g., MainWindow.axaml.cs) before the command is invoked.
    /// </summary>
    public Func<Task>? ShowOptionsDialogAsync { get; set; }

    /// <summary>Initializes with the PhotonTorch export ViewModel.</summary>
    /// <param name="vm">Provides export settings and the core export command.</param>
    public PhotonTorchExportFormat(PhotonTorchExportViewModel vm)
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
