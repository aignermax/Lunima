using Avalonia.Controls;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Tool window for running a wavelength sweep on a selected ONA Analyzer
/// component. Hosts the sweep parameters, the live plot, and run/cancel/export
/// controls. Opened from MainWindow when the user invokes the "ONA Sweep"
/// action with an analyzer placed on the canvas.
/// </summary>
public partial class OnaAnalyzerWindow : Window
{
    /// <summary>Initialises the ONA Analyzer tool window.</summary>
    public OnaAnalyzerWindow()
    {
        InitializeComponent();
    }
}
