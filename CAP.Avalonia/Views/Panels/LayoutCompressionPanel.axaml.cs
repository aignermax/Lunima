using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Panel for layout compression — minimizes chip area while maintaining connectivity.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class LayoutCompressionPanel : UserControl
{
    /// <summary>Initializes the LayoutCompressionPanel.</summary>
    public LayoutCompressionPanel()
    {
        InitializeComponent();
    }
}
