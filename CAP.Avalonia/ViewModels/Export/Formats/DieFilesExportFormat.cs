using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export format "one file per die": every die of a multi-die assembly as its own Nazca
/// script / GDS in die-local coordinates (the regular Nazca export writes the whole
/// assembly with one cell per die).
/// </summary>
public class DieFilesExportFormat : IExportFormat
{
    /// <summary>Initializes with <c>FileOperationsViewModel.ExportDieFilesCommand</c>.</summary>
    public DieFilesExportFormat(IAsyncRelayCommand exportCommand)
    {
        ExportCommand = exportCommand;
    }

    /// <inheritdoc/>
    public string Name => Services.Localization.LocalizationService.Instance.Translate("Dies.Export.Name");

    /// <inheritdoc/>
    public string Icon => "🧩";

    /// <inheritdoc/>
    public string Description => Services.Localization.LocalizationService.Instance.Translate("Dies.Export.Description");

    /// <inheritdoc/>
    public string Background => "#3d5d4d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand { get; }
}
