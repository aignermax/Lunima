using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace CAP.Avalonia.Views;

/// <summary>
/// Partial portion of <see cref="MainWindow"/> that wires up the Settings
/// window opener. Split out of the main code-behind so the file stays under
/// the 500-line production cap.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Configures <see cref="MainViewModel.ShowSettingsWindowAsync"/> so that
    /// gear-icon / shortcut clicks open the Settings window — creating or
    /// reusing a single instance, optionally pre-selecting a target page.
    /// Any failure during DI resolution of a page is caught and surfaced on
    /// the status bar instead of crashing the UI thread.
    /// </summary>
    private void WireSettingsOpener(MainViewModel vm)
    {
        vm.ShowSettingsWindowAsync = async (pageType) =>
        {
            try
            {
                SettingsWindowViewModel settingsVm;
                if (_settingsWindow != null && _settingsWindow.IsVisible)
                {
                    _settingsWindow.Activate();
                    settingsVm = (SettingsWindowViewModel)_settingsWindow.DataContext!;
                }
                else
                {
                    settingsVm = App.Services.GetRequiredService<SettingsWindowViewModel>();
                    _settingsWindow = new SettingsWindow { DataContext = settingsVm };
                    _settingsWindow.Show(this);
                }

                if (pageType != null)
                {
                    var target = settingsVm.Pages.FirstOrDefault(p => p.GetType() == pageType);
                    if (target != null)
                        settingsVm.SelectedPage = target;
                    else
                        vm.StatusText = $"Settings page '{pageType.Name}' not found — opening default.";
                }
            }
            catch (System.Exception ex)
            {
                vm.StatusText = $"Failed to open Settings: {ex.Message}";
            }
            await System.Threading.Tasks.Task.CompletedTask;
        };
    }
}
