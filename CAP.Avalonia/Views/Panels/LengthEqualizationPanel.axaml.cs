using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Right-panel section shown for a multi-selection of connections: gives all of them one common
/// length in one click. Binds to <see cref="CAP.Avalonia.ViewModels.MainViewModel"/> like the other panels.
/// </summary>
public partial class LengthEqualizationPanel : UserControl
{
    /// <summary>Initializes a new instance of <see cref="LengthEqualizationPanel"/>.</summary>
    public LengthEqualizationPanel()
    {
        InitializeComponent();
    }
}
