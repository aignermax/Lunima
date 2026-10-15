using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels.LogicTab;

/// <summary>
/// Logic tab column showing the live gate outputs and the held register state.
/// DataContext is inherited from the hosting LogicPanel (MainViewModel).
/// </summary>
public partial class LogicOutputsColumn : UserControl
{
    /// <summary>Initializes the LogicOutputsColumn.</summary>
    public LogicOutputsColumn()
    {
        InitializeComponent();
    }
}
