using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Non-modal card for the "Store a number in light" tour (issue #1422), anchored
/// at the bottom centre of the canvas overlay in <c>MainWindow.axaml</c>. All
/// logic lives in
/// <see cref="ViewModels.Onboarding.FirstStepsTutorial.StoreNumberTourViewModel"/>.
/// </summary>
public partial class StoreNumberTourPanel : UserControl
{
    /// <summary>Initialises the panel.</summary>
    public StoreNumberTourPanel()
    {
        InitializeComponent();
    }
}
