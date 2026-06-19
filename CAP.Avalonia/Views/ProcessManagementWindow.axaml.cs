using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CAP.Avalonia.Views;

/// <summary>
/// Window for viewing and adjusting a fabrication process (layer stack,
/// cross-sections, materials). DataContext is a <c>ProcessManagementViewModel</c>.
/// </summary>
public partial class ProcessManagementWindow : Window
{
    /// <summary>Initialises the window.</summary>
    public ProcessManagementWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
