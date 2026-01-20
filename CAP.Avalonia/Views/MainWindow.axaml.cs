using Avalonia.Controls;
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
}
