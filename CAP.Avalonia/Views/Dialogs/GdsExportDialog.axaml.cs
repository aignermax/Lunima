using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Dialog window showing GDS export configuration (Python path, environment check).
/// DataContext must be set to <see cref="CAP.Avalonia.ViewModels.Export.GdsExportViewModel"/>.
/// </summary>
public partial class GdsExportDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public GdsExportDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
