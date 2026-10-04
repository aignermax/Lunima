using Avalonia.Controls;

namespace CAP.Avalonia.Controls.Help;

/// <summary>
/// Plain-language explainer for waveguide-crossing findings in the Design Checks tab
/// (#1391): why a plain X junction fails (light scatters into the wrong arm, the layout
/// fails foundry overlap checks) and what to do instead (place a crossing component
/// such as the SiEPIC EBeam "Crossing 4-Port" and route both arms through it). The
/// looping <see cref="HelpAnimations.WaveguideCrossingHelpAnimation"/> shows both cases
/// side by side. Opened from the (?) button next to the findings list.
/// </summary>
public partial class WaveguideCrossingHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public WaveguideCrossingHelpFlyout()
    {
        InitializeComponent();
    }
}
