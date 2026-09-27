using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Parameter Sweep tab of the analysis dock: sweeps the selected component's
/// slider parameter over a range.
/// </summary>
public partial class ParameterSweepPanel : UserControl
{
    /// <summary>Initializes the panel.</summary>
    public ParameterSweepPanel()
    {
        InitializeComponent();
    }
}
