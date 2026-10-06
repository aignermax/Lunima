using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Non-modal card for the "Run a program on your chip" tour (issue #1267),
/// anchored by the tour overlays in <c>MainWindow.axaml</c> and
/// <c>IsaPlaygroundWindow.axaml</c>. All logic lives in
/// <see cref="ViewModels.Onboarding.FirstStepsTutorial.RunProgramTourViewModel"/>.
/// </summary>
public partial class RunProgramTourPanel : UserControl
{
    /// <summary>Initialises the panel.</summary>
    public RunProgramTourPanel()
    {
        InitializeComponent();
    }
}
