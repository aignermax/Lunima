namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for general application preferences that do not belong to a
/// dedicated category. Acts as an anchor point for future general preferences.
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
    public object ViewModel { get; } = new GeneralSettingsViewModel();
}
