using CAP.Avalonia.ViewModels.Export;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for Python interpreter discovery and environment validation.
/// Reuses the existing <see cref="GdsExportViewModel"/> which owns the Python-path
/// property and Nazca availability check — changes apply immediately to GDS export.
/// </summary>
public class PythonEnvironmentSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "Python Environment";

    /// <inheritdoc/>
    public string Icon => "🐍";

    /// <inheritdoc/>
    public string? Category => "Export";

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="PythonEnvironmentSettingsPage"/>.
    /// </summary>
    public PythonEnvironmentSettingsPage(GdsExportViewModel gdsExportViewModel)
    {
        ViewModel = gdsExportViewModel;
    }
}
