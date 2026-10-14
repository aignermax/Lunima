using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Right-panel section showing the insertion-loss breakdown (propagation, bends, junctions,
/// total) of the selected waveguide connection. Binds to
/// <see cref="CAP.Avalonia.ViewModels.MainViewModel"/> like the other panels.
/// </summary>
public partial class ConnectionLossPanel : UserControl
{
    /// <summary>Initializes a new instance of <see cref="ConnectionLossPanel"/>.</summary>
    public ConnectionLossPanel()
    {
        InitializeComponent();
    }
}
