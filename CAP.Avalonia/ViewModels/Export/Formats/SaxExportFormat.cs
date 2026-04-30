using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format for SAX (Simphony-compatible) Python circuit-simulator scripts.
/// Originally introduced under the name "PICWave" because the issue (#474)
/// targeted that commercial tool, but the actual exporter has always emitted
/// sax-based Python — see <c>SaxScriptWriter</c>. The label was renamed to
/// match what the script actually does.
/// </summary>
public class SaxExportFormat : IExportFormat
{
    private readonly IAsyncRelayCommand _exportCommand;

    /// <inheritdoc/>
    public string Name => "SAX (Simphony)";

    /// <inheritdoc/>
    public string Icon => "🌊";

    /// <inheritdoc/>
    public string Description => "Export to a SAX/Simphony-compatible Python circuit-simulator script";

    /// <inheritdoc/>
    public string Background => "#3d4d5d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand => _exportCommand;

    /// <summary>
    /// Initializes with the SAX export command from <c>FileOperationsViewModel.ExportSaxCommand</c>.
    /// </summary>
    /// <param name="exportCommand">The async command that performs the full SAX export flow.</param>
    public SaxExportFormat(IAsyncRelayCommand exportCommand)
    {
        _exportCommand = exportCommand;
    }
}
