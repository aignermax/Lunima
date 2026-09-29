using Avalonia.Controls;
using Avalonia.Input;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;

namespace CAP.Avalonia.Views;

public partial class NewComponentWindow : Window
{
    public NewComponentWindow()
    {
        InitializeComponent();
    }

    private void OnPreviewThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is NewComponentViewModel vm && vm.PreviewBitmap is { } bitmap)
        {
            new ComponentPreviewWindow(bitmap).Show(this);
        }
    }
}
