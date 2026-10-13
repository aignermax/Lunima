using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Help of the Make-gate steps: short sections on pin roles, threshold and margin,
/// each carried by a looping animation (interference of A, B and BIAS in a coupler;
/// output power crossing the threshold). Opened from the panel's <c>HelpFlyoutButton</c>.
/// </summary>
public partial class MakeGateHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public MakeGateHelpFlyout()
    {
        InitializeComponent();
    }
}
