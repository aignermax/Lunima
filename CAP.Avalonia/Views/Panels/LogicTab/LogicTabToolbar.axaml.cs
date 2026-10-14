using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels.LogicTab;

/// <summary>
/// The Logic tab's single action row: build, register clock and replay actions plus the status line, wrapping as groups on a narrow dock.
/// DataContext is inherited from the hosting LogicPanel (MainViewModel).
/// </summary>
public partial class LogicTabToolbar : UserControl
{
    /// <summary>Initializes the LogicTabToolbar.</summary>
    public LogicTabToolbar()
    {
        InitializeComponent();
    }
}
