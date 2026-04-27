using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for PhotonTorch GPU-simulation scripts.
/// Opens an options dialog so users can configure wavelength and simulation mode
/// before exporting.
/// </summary>
public class PhotonTorchExportFormat : IExportFormat
{
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
    /// Must be set from the UI layer (e.g., MainWindow.axaml.cs::WireExportDialogs)
    /// before the command is invoked. Invoking the command before this is wired
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public Func<Task>? ShowOptionsDialogAsync { get; set; }

    /// <summary>Initializes the PhotonTorch export format adapter.</summary>
    public PhotonTorchExportFormat()
    {
        _exportCommand = new AsyncRelayCommand(RunExportFlowAsync);
    }

    private async Task RunExportFlowAsync()
    {
        if (ShowOptionsDialogAsync == null)
            throw new InvalidOperationException(
                $"{nameof(PhotonTorchExportFormat)}.{nameof(ShowOptionsDialogAsync)} has not been wired. " +
                "The UI layer (MainWindow.axaml.cs::WireExportDialogs) must set this callback before the export command can run.");
        await ShowOptionsDialogAsync();
    }
}
