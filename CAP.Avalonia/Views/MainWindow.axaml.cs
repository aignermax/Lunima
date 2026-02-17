using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set up the FileDialogService when the window is loaded
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.FileDialogService = new FileDialogService(this);
            }
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        // Global keyboard shortcuts that work regardless of focus
        switch (e.Key)
        {
            case Key.F:
                if (DataContext is MainViewModel vm)
                {
                    vm.ZoomToFit(DesignCanvasControl.Bounds.Width, DesignCanvasControl.Bounds.Height);
                    e.Handled = true;
                }
                break;
        }
    }

    private void ZoomToFitButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ZoomToFit(DesignCanvasControl.Bounds.Width, DesignCanvasControl.Bounds.Height);
        }
    }
}
