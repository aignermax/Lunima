using Avalonia.Controls;
using Avalonia.Input;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;

namespace CAP.Avalonia.Views;

public partial class NewComponentWindow : Window
{
    private bool _closeApproved;
    private Task? _retirement;
    internal bool IsRetiring => _retirement != null;
    public NewComponentWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeApproved) return;
        e.Cancel = true;
        if (_retirement != null) return;
        IsEnabled = false;
        try
        {
            _retirement = (DataContext as NewComponentViewModel)?.StopAsync() ?? Task.CompletedTask;
            await _retirement;
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError($"Component editor retirement failed: {error}"); }
        finally { _closeApproved = true; Close(); }
    }

    private void OnPreviewThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is NewComponentViewModel vm && vm.PreviewBitmap is { } bitmap)
        {
            new ComponentPreviewWindow(bitmap).Show(this);
        }
    }
}
