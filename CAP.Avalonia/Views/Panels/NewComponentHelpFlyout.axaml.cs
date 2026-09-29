using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Structure-code explainer for the New component window (#1171): short sections
/// (#1152 text budget), a looping "code → component → canvas" diagram, and the
/// copyable gdsfactory / Nazca examples. Opened from the window's
/// <c>HelpFlyoutButton</c> next to the structure-code label.
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
