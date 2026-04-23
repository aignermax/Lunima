using CAP.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// ViewModel for general application preferences backed by
/// <see cref="UserPreferencesService"/>.
/// Currently exposes the custom Python path as a convenience;
/// additional preferences can be surfaced here without new pages.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly UserPreferencesService _preferences;

    [ObservableProperty]
    private string _customPythonPath;

    /// <summary>
    /// Initializes a new instance of <see cref="GeneralSettingsViewModel"/>.
    /// </summary>
    public GeneralSettingsViewModel(UserPreferencesService preferences)
    {
        _preferences = preferences;
        _customPythonPath = preferences.GetCustomPythonPath() ?? string.Empty;
    }

    partial void OnCustomPythonPathChanged(string value)
    {
        _preferences.SetCustomPythonPath(string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>Clears the stored custom Python path so auto-detection is used.</summary>
    [RelayCommand]
    private void ClearPythonPath()
    {
        CustomPythonPath = string.Empty;
    }
}
