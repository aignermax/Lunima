using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CAP.Avalonia.Views;

/// <summary>
/// Code-behind for the HierarchyPanel view.
/// Displays the component hierarchy in a tree structure.
/// </summary>
public partial class HierarchyPanel : UserControl
{
    public HierarchyPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
