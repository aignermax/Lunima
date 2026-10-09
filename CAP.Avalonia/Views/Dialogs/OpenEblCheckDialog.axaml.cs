using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels.Export.OpenEbl;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Dialog window for "Export → Check for openEBL…" (issue #1361): exports the current
/// design to GDS and runs the openEBL submission + verification checks on it.
/// DataContext must be set to <see cref="OpenEblCheckViewModel"/>.
/// </summary>
public partial class OpenEblCheckDialog : Window
{
    /// <summary>Initializes the dialog and bridges the ViewModel's clipboard callback.</summary>
    public OpenEblCheckDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is OpenEblCheckViewModel vm)
                vm.CopyToClipboard = async text =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(text);
                };
        };
    }

    /// <summary>Closing mid-run cancels the check so no orphaned Python process keeps running.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is OpenEblCheckViewModel { IsChecking: true } vm)
            vm.CancelCommand.Execute(null);
        base.OnClosing(e);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
