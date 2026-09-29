using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Structure-code help for the New component window: what the Python editor expects
/// (a <c>component</c> variable matching the selected backend), a looping
/// code → component → canvas animation, and the copyable minimal gdsfactory / Nazca
/// examples. Opened from the window's "?" button next to the Structure label.
/// </summary>
public partial class NewComponentHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public NewComponentHelpFlyout()
    {
        InitializeComponent();
    }

    private void OnCopyGdsFactoryExample(object? sender, RoutedEventArgs e) => CopyToClipboard(GdsFactoryExampleBox.Text);

    private void OnCopyNazcaExample(object? sender, RoutedEventArgs e) => CopyToClipboard(NazcaExampleBox.Text);

    private void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }
}
