using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Code-behind for the click-to-assign layer picker (issue #1173). The window
/// is a pure view over <see cref="ViewModels.GdsImport.LayerPicker.GdsLayerPickerViewModel"/>;
/// every assignment applies immediately, so Close is the only exit.
/// </summary>
public partial class GdsLayerPickerWindow : Window
{
    /// <summary>Initializes a new <see cref="GdsLayerPickerWindow"/>.</summary>
    public GdsLayerPickerWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
