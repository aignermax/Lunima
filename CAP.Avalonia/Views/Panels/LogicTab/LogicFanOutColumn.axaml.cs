using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels.LogicTab;

/// <summary>
/// Logic tab column listing the optical fan-out warnings of the built network.
/// DataContext is inherited from the hosting LogicPanel (MainViewModel).
/// </summary>
public partial class LogicFanOutColumn : UserControl
{
    /// <summary>Initializes the LogicFanOutColumn.</summary>
    public LogicFanOutColumn()
    {
        InitializeComponent();
    }
}
