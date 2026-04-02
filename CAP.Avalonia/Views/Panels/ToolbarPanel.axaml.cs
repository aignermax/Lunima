using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Toolbar panel containing mode buttons, zoom controls, undo/redo, file operations, PDK, and simulation.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class ToolbarPanel : UserControl
{
    /// <summary>Initializes the ToolbarPanel.</summary>
    public ToolbarPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles the Zoom-to-Fit button click. Delegates to MainViewModel using the
    /// already-wired ViewportControl.GetViewportSize callback.
    /// </summary>
    private void ZoomToFitButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var (width, height) = vm.ViewportControl.GetViewportSize?.Invoke() ?? (1400, 900);
            vm.ZoomToFit(width, height);
        }
    }
}
