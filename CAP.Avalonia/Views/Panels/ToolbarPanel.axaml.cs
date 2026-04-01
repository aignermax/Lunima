using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Toolbar panel with mode buttons, zoom controls, undo/redo, file operations, PDK loading, and simulation.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class ToolbarPanel : UserControl
{
    /// <summary>Initializes the ToolbarPanel.</summary>
    public ToolbarPanel()
    {
        InitializeComponent();
        ZoomToFitButton.Click += OnZoomToFitButtonClick;
    }

    private void OnZoomToFitButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var (width, height) = vm.ViewportControl.GetViewportSize?.Invoke() ?? (1400.0, 900.0);
        vm.ZoomToFit(width, height);
    }
}
