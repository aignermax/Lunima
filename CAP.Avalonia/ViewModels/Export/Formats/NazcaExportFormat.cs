using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for Nazca Python scripts (with optional GDS auto-generation).
/// </summary>
public class NazcaExportFormat : IExportFormat
{
    private readonly IAsyncRelayCommand _exportCommand;

    /// <inheritdoc/>
    public string Name => "Nazca Python";

    /// <inheritdoc/>
    public string Icon => "🐍";

    /// <inheritdoc/>
    public string Description => "Export to Nazca Python script for photonic layout (+ optional GDS)";

    /// <inheritdoc/>
    public string Background => "#3d5d3d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand => _exportCommand;

    /// <summary>
    /// Initializes with the Nazca export command from <c>FileOperationsViewModel.ExportNazcaCommand</c>.
    /// </summary>
    /// <param name="exportCommand">The async command that performs the full Nazca export flow.</param>
    public NazcaExportFormat(IAsyncRelayCommand exportCommand)
    {
        _exportCommand = exportCommand;
    }
}
