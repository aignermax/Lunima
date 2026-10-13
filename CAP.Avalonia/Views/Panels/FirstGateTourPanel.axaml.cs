using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Non-modal card for the "Your first gate" tour, anchored by the
/// <c>TourAnchorOverlay</c> in <c>MainWindow.axaml</c>. All logic lives in
/// <see cref="ViewModels.Onboarding.FirstStepsTutorial.FirstGateTourViewModel"/>.
/// </summary>
public partial class FirstGateTourPanel : UserControl
{
    /// <summary>Initialises the panel.</summary>
    public FirstGateTourPanel()
    {
        InitializeComponent();
    }
}
