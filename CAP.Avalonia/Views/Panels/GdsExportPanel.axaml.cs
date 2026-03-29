using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for GDS export configuration — manages Python path, Nazca detection, and GDS generation.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class GdsExportPanel : UserControl
{
    /// <summary>Initializes the GdsExportPanel.</summary>
    public GdsExportPanel()
    {
        InitializeComponent();
    }
}
