using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Non-modal card for the "Connect two chiplets" tour (issue #1288), anchored
/// by the tour overlay in <c>MainWindow.axaml</c>. All logic lives in
/// <see cref="ViewModels.Onboarding.FirstStepsTutorial.ConnectChipletsTourViewModel"/>.
/// </summary>
public partial class ConnectChipletsTourPanel : UserControl
{
    /// <summary>Initialises the panel.</summary>
    public ConnectChipletsTourPanel()
    {
        InitializeComponent();
    }
}
