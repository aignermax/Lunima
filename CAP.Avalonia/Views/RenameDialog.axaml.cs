using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views;

/// <summary>
/// Simple dialog window for entering a new name when renaming a group template.
/// Returns the new name string on confirmation, or null on cancellation.
/// </summary>
public partial class RenameDialog : Window
{
    /// <summary>
    /// Initializes the rename dialog with the current name pre-filled.
    /// </summary>
    /// <param name="currentName">The current template name to pre-fill in the text box.</param>
    public RenameDialog(string currentName)
    {
        InitializeComponent();
        NameTextBox.Text = currentName;
        NameTextBox.SelectAll();
        Opened += (_, _) => NameTextBox.Focus();
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        Close(NameTextBox.Text?.Trim());
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
