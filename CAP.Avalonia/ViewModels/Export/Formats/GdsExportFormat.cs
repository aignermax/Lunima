using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for GDS layout files via Python/Nazca.
/// Opens a configuration dialog (Python path, environment check) before
/// triggering the Nazca → GDS export pipeline.
/// </summary>
public class GdsExportFormat : IExportFormat
{
    private readonly AsyncRelayCommand _exportCommand;

    /// <inheritdoc/>
    public string Name => "GDS Layout";

    /// <inheritdoc/>
    public string Icon => "📐";

    /// <inheritdoc/>
    public string Description => "Export GDS layout via Nazca Python (requires Python + Nazca)";

    /// <inheritdoc/>
    public string Background => "#3d5d5d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand => _exportCommand;

    /// <summary>
    /// Callback that opens the GDS configuration dialog.
    /// Must be set from the UI layer (e.g., MainWindow.axaml.cs::WireExportDialogs)
    /// before the command is invoked. Invoking the command before this is wired
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public Func<Task>? ShowOptionsDialogAsync { get; set; }

    /// <summary>Initializes the GDS export format adapter.</summary>
    public GdsExportFormat()
    {
        _exportCommand = new AsyncRelayCommand(RunExportFlowAsync);
    }

    private async Task RunExportFlowAsync()
    {
        if (ShowOptionsDialogAsync == null)
            throw new InvalidOperationException(
                $"{nameof(GdsExportFormat)}.{nameof(ShowOptionsDialogAsync)} has not been wired. " +
                "The UI layer (MainWindow.axaml.cs::WireExportDialogs) must set this callback before the export command can run.");
        await ShowOptionsDialogAsync();
    }
}
