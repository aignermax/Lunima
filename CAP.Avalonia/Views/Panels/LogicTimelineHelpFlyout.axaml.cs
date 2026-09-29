using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Event-timeline help of the Logic tab (#1206): short sections (#1152 text budget)
/// plus a looping pulse-arrival diagram (badges flip and a waveform lane steps as the
/// pulse reaches each gate). Opened from the "?" next to the Timeline section title.
/// </summary>
public partial class LogicTimelineHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public LogicTimelineHelpFlyout()
    {
        InitializeComponent();
    }
}
