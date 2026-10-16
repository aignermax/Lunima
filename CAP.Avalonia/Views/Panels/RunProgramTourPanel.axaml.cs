using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Non-modal card for the "Run a program on your chip" tour (issue #1267),
/// anchored by the tour overlays in <c>MainWindow.axaml</c> and
/// <c>IsaPlaygroundWindow.axaml</c>. All logic lives in
/// <see cref="ViewModels.Onboarding.FirstStepsTutorial.RunProgramTourViewModel"/>.
/// </summary>
public partial class RunProgramTourPanel : UserControl
{
    public event EventHandler<RoutedEventArgs>? OpenPlaygroundRequested;

    private void OpenPlayground_Click(object? sender, RoutedEventArgs e) =>
        OpenPlaygroundRequested?.Invoke(this, e);

    /// <summary>Initialises the panel.</summary>
    public RunProgramTourPanel()
    {
        InitializeComponent();
    }
}
