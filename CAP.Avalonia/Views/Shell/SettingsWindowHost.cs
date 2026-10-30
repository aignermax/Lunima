using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Settings;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Owns the single Settings window and optional page navigation.</summary>
internal sealed class SettingsWindowHost(Window owner,
    Func<CAP.Avalonia.ViewModels.Settings.SettingsWindowViewModel> createViewModel, Action<string> setStatus)
{
    private readonly Window _owner = owner;
    private readonly Func<CAP.Avalonia.ViewModels.Settings.SettingsWindowViewModel> _createViewModel = createViewModel;
    private readonly Action<string> _setStatus = setStatus;
    private SettingsWindow? _settingsWindow;

    public async Task ShowAsync(Type? pageType)
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
                settingsVm = _createViewModel();
                _settingsWindow = new SettingsWindow { DataContext = settingsVm };
                _settingsWindow.Show(_owner);
            }

            if (pageType != null)
            {
                var target = settingsVm.Pages.FirstOrDefault(p => p.GetType() == pageType);
                if (target != null)
                    settingsVm.SelectedPage = target;
                else
                    _setStatus($"Settings page '{pageType.Name}' not found — opening default.");
            }
        }
        catch (System.Exception ex)
        {
            _setStatus($"Failed to open Settings: {ex.Message}");
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

}
