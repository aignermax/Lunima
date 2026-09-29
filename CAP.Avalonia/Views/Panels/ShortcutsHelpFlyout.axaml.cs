using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Full keyboard-shortcut reference (#1162), opened from the "?" button in the status
/// bar. The bar itself shows only the hints relevant to the current canvas mode, so the
/// complete list moves here where space is not constrained.
/// </summary>
public partial class ShortcutsHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public ShortcutsHelpFlyout()
    {
        InitializeComponent();
    }
}
