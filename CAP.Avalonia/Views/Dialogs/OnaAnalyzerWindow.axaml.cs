using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Tool window for running a wavelength sweep on a selected ONA Analyzer
/// component. Hosts the sweep parameters, the live plot, and run/cancel/export
/// controls. Opened from MainWindow when the user invokes the "ONA Sweep"
/// action with an analyzer placed on the canvas.
/// </summary>
public partial class OnaAnalyzerWindow : Window
{
    private bool _closeApproved;
    private Task? _retirement;
    /// <summary>Initialises the ONA Analyzer tool window.</summary>
    public OnaAnalyzerWindow()
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
            _retirement = (DataContext as OnaSweepViewModel)?.StopAsync() ?? Task.CompletedTask;
            await _retirement;
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError($"ONA window retirement failed: {error}"); }
        finally { _closeApproved = true; Close(); }
    }
}
