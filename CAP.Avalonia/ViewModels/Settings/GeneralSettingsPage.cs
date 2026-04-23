using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for general application preferences backed by
/// <see cref="UserPreferencesService"/> that do not belong to a dedicated category.
/// </summary>
public class GeneralSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "General";

    /// <inheritdoc/>
    public string Icon => "⚙";

    /// <inheritdoc/>
    public string? Category => null;

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="GeneralSettingsPage"/>.
    /// </summary>
    public GeneralSettingsPage(UserPreferencesService preferences)
    {
        ViewModel = new GeneralSettingsViewModel(preferences);
    }
}
