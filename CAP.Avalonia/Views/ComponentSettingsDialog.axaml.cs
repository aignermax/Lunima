using Avalonia.Controls;
using CAP.Avalonia.ViewModels.ComponentSettings;

namespace CAP.Avalonia.Views;

/// <summary>
/// Code-behind for the Component Settings dialog window.
/// </summary>
public partial class ComponentSettingsDialog : Window
{
    private bool _closeApproved;
    private Task? _retirement;
    /// <summary>Initialises the Component Settings dialog.</summary>
    public ComponentSettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Cancels a running FDTD recompute when the dialog is closed — otherwise the
    /// solve (and its Docker container) would keep running in the background.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeApproved) return;
        e.Cancel = true;
        if (_retirement != null) return;
        IsEnabled = false;
        try
        {
            _retirement = (DataContext as ComponentSettingsDialogViewModel)?.StopAsync() ?? Task.CompletedTask;
            await _retirement;
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError($"Component settings retirement failed: {error}"); }
        finally { _closeApproved = true; Close(); }
    }
}
