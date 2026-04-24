using CAP.Avalonia.ViewModels.Export;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for PhotonTorch export configuration — simulation wavelength,
/// steady-state vs time-domain mode, and time-domain parameters (bit rate,
/// step count). The export trigger lives in the top toolbar; this page owns
/// only the configuration so the right panel stays analysis-focused.
/// </summary>
public class PhotonTorchExportSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "PhotonTorch Export";

    /// <inheritdoc/>
    public string Icon => "🔦";

    /// <inheritdoc/>
    public string? Category => "Export";

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="PhotonTorchExportSettingsPage"/>.
    /// </summary>
    public PhotonTorchExportSettingsPage(PhotonTorchExportViewModel photonTorchExportViewModel)
    {
        ViewModel = photonTorchExportViewModel;
    }
}
