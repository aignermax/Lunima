using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Help of the Wavelength Spectrum coherent-interference toggle (#1333): short
/// sections (#1152 text budget) plus a looping MZI diagram — wavefronts travel both
/// arms, the longer meander arm's wave lags, and the swept wavelength turns that
/// phase lag into fringes (bright/dim output, marker on the fringe curve). Opened
/// from the <c>HelpFlyoutButton</c> next to the toggle in the Spectrum tab.
/// </summary>
public partial class SpectrumCoherentHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public SpectrumCoherentHelpFlyout()
    {
        InitializeComponent();
    }
}
