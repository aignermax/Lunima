using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.ViewModels.Hierarchy;

namespace CAP.Avalonia.Views;

/// <summary>
/// Code-behind for HierarchyPanel view.
/// Handles pointer events for tree node selection and inline rename.
/// </summary>
public partial class HierarchyPanel : UserControl
{
    public HierarchyPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles pointer press on a tree node.
    /// Single click selects the component; double-click starts inline rename.
    /// </summary>
    private void TreeNode_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not HierarchyNodeViewModel node)
            return;

        if (e.ClickCount == 2)
        {
            node.StartRenameCommand.Execute(null);

            // Defer focus so the TextBox is visible before we focus it
            Dispatcher.UIThread.Post(() =>
            {
                var textBox = border.FindDescendantOfType<TextBox>();
                if (textBox?.IsVisible == true)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            });
        }
        else
        {
            node.SelectCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Handles Enter (confirm) and Escape (cancel) keys in the rename TextBox.
    /// </summary>
    private void RenameTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not HierarchyNodeViewModel node)
            return;

        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            node.ConfirmRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            node.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Confirms the rename when the TextBox loses focus (e.g., user clicks elsewhere).
    /// </summary>
    private void RenameTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is HierarchyNodeViewModel node && node.IsRenaming)
        {
            node.ConfirmRenameCommand.Execute(null);
        }
    }
}
