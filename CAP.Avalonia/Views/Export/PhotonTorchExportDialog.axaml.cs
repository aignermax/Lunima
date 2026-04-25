using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Export;

/// <summary>
/// Modal dialog for per-export PhotonTorch configuration.
/// Shows wavelength, simulation mode (steady-state vs time-domain), and
/// time-domain parameters so the user can review them before triggering the
/// export. Returns <c>true</c> when the user confirms; <c>false</c> on cancel.
/// </summary>
public partial class PhotonTorchExportDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public PhotonTorchExportDialog()
    {
        InitializeComponent();
    }

    private void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
