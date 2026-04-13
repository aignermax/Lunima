using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for PhotonTorch export configuration — simulation mode, wavelength, and time-domain settings.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class PhotonTorchExportPanel : UserControl
{
    /// <summary>Initializes the PhotonTorchExportPanel.</summary>
    public PhotonTorchExportPanel()
    {
        InitializeComponent();
    }
}
