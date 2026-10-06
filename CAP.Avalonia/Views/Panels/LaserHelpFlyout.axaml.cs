using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Help of the laser/light-source editor in the properties panel (#1220): short
/// sections (#1152 text budget) plus a looping "light enters the circuit" diagram —
/// the laser switches on, a pulse travels into a splitter, the strong/weak branches
/// glow with the power-ramp colors and the far coupler stays an unlit output. Opened
/// from the <c>HelpFlyoutButton</c> next to the laser on/off switch.
/// </summary>
public partial class LaserHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LaserHelpFlyout()
    {
        InitializeComponent();
    }
}
