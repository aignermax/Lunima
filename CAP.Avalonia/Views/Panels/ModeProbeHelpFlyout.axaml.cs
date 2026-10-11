using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Plain-language explainer for the Mode Probe flyout (#1486): what a guided mode and
/// n_eff are, why n_g exceeds n_eff and sets the timing-panel delays, and what MFD and
/// fiber-coupling efficiency mean — with a small looping animation of a confined mode
/// spot and a wave crest creeping along the guide. Opened from the probe header's "?"
/// button (same pattern as <see cref="EyeHelpFlyout"/>).
/// </summary>
public partial class ModeProbeHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public ModeProbeHelpFlyout()
    {
        InitializeComponent();
    }
}
