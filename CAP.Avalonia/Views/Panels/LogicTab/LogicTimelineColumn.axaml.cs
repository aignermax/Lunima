using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels.LogicTab;

/// <summary>
/// Logic tab column listing the switch events of the last input toggle; clicking a row replays that instant.
/// DataContext is inherited from the hosting LogicPanel (MainViewModel).
/// </summary>
public partial class LogicTimelineColumn : UserControl
{
    /// <summary>Initializes the LogicTimelineColumn.</summary>
    public LogicTimelineColumn()
    {
        InitializeComponent();
    }
}
