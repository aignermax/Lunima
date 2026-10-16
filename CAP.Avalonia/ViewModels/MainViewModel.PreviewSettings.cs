using CAP.Avalonia.ViewModels.Settings;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel
{
    /// <summary>Opens the existing interpreter setup page from the library's preview hint.</summary>
    [RelayCommand]
    private async Task OpenPreviewPythonSettings()
    {
        if (ShowSettingsWindowAsync != null)
            await ShowSettingsWindowAsync(typeof(PythonEnvironmentsSettingsPage));
    }
}
