using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Settings;

namespace CAP.Avalonia.Views;

/// <summary>
/// Settings window that hosts the settings registry navigation panel
/// and renders the selected <see cref="ISettingsPage"/> content area.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>Initializes a new instance of <see cref="SettingsWindow"/>.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }
}
