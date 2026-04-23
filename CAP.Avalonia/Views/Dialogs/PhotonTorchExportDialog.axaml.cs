using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Dialog window showing PhotonTorch export options.
/// DataContext must be set to <see cref="CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel"/>.
/// </summary>
public partial class PhotonTorchExportDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public PhotonTorchExportDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
