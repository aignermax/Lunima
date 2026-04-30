using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels.ComponentSettings;

namespace CAP.Avalonia.Views;

/// <summary>
/// Modal dialog asking the user to map imported S-parameter port names onto
/// the component's pin names. Returns the selected mapping via
/// <see cref="ShowDialog"/>; null on Cancel.
/// </summary>
public partial class PortMappingDialog : Window
{
    public PortMappingDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PortMappingDialogViewModel vm)
        {
            Close(null);
            return;
        }

        var mapping = vm.BuildResultOrNull(out var error);
        if (mapping == null)
        {
            // Surface the validation reason in-dialog rather than closing
            // with an inscrutable null result. The error TextBlock binding
            // takes care of showing/hiding the panel.
            if (this.FindControl<TextBlock>("ErrorText") is { } errorBlock)
                errorBlock.Text = error;
            return;
        }

        Close(mapping);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
