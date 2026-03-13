using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels.Hierarchy;

namespace CAP.Avalonia.Views;

/// <summary>
/// Code-behind for HierarchyPanel view.
/// Handles pointer events for tree node selection.
/// </summary>
public partial class HierarchyPanel : UserControl
{
    public HierarchyPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles pointer press on a tree node to trigger selection.
    /// </summary>
    private void TreeNode_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is HierarchyNodeViewModel node)
        {
            // Select the component on the canvas
            node.SelectCommand.Execute(null);
            e.Handled = true;
        }
    }
}
