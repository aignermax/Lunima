using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for PICWave Python circuit-simulator scripts.
/// </summary>
public class PicWaveExportFormat : IExportFormat
{
    private readonly IAsyncRelayCommand _exportCommand;

    /// <inheritdoc/>
    public string Name => "PICWave";

    /// <inheritdoc/>
    public string Icon => "🌊";

    /// <inheritdoc/>
    public string Description => "Export to PICWave Python circuit simulator";

    /// <inheritdoc/>
    public string Background => "#3d4d5d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand => _exportCommand;

    /// <summary>
    /// Initializes with the PICWave export command from <c>FileOperationsViewModel.ExportPicWaveCommand</c>.
    /// </summary>
    /// <param name="exportCommand">The async command that performs the full PICWave export flow.</param>
    public PicWaveExportFormat(IAsyncRelayCommand exportCommand)
    {
        _exportCommand = exportCommand;
    }
}
