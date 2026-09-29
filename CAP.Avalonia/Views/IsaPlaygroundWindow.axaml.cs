using Avalonia.Controls;

namespace CAP.Avalonia.Views;

/// <summary>
/// Non-modal ISA playground tool window (issue #1194): write a 4-bit assembly
/// program (or pick a shipped sample), assemble it and step the golden-model
/// machine while the state readout and the current source line stay visible.
/// Opened from the Tools flyout; <see cref="MainWindow"/> deduplicates so a
/// second open activates the existing window.
/// </summary>
public partial class IsaPlaygroundWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public IsaPlaygroundWindow()
    {
        InitializeComponent();
    }
}
