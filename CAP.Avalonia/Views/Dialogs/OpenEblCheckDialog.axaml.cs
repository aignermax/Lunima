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
    private OpenEblCheckViewModel? _clipboardOwner;
    private Func<string, Task>? _clipboardCallback;
    private Task? _retirement;
    private bool _closeApproved;

    /// <summary>Initializes the dialog and bridges the ViewModel's clipboard callback.</summary>
    public OpenEblCheckDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        ReleaseClipboard();
        if (DataContext is not OpenEblCheckViewModel model) return;
        _clipboardOwner = model;
        _clipboardCallback = async text =>
        {
            var clipboard = Clipboard;
            if (clipboard != null) await clipboard.SetTextAsync(text);
        };
        model.CopyToClipboard = _clipboardCallback;
    }

    private void ReleaseClipboard()
    {
        if (_clipboardOwner != null && ReferenceEquals(_clipboardOwner.CopyToClipboard, _clipboardCallback))
            _clipboardOwner.CopyToClipboard = null;
        _clipboardOwner = null;
        _clipboardCallback = null;
    }

    /// <summary>An approved close cancels and drains this session without retiring the reusable feature model.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeApproved) return;
        e.Cancel = true;
        if (_retirement != null) return;
        IsEnabled = false;
        try
        {
            _retirement = (DataContext as OpenEblCheckViewModel)?.CancelAndDrainAsync() ?? Task.CompletedTask;
            await _retirement;
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError($"openEBL window cleanup failed: {error}"); }
        finally { _closeApproved = true; Close(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        ReleaseClipboard();
        base.OnClosed(e);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
