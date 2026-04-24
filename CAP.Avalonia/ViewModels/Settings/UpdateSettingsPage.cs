using CAP.Avalonia.ViewModels.Update;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for software update configuration.
/// Delegates to the shared <see cref="UpdateViewModel"/> so
/// startup-check state is shared with the main-window update banner.
/// </summary>
public class UpdateSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "Software Updates";

    /// <inheritdoc/>
    public string Icon => "🔄";

    /// <inheritdoc/>
    public string? Category => null;

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="UpdateSettingsPage"/>.
    /// </summary>
    public UpdateSettingsPage(UpdateViewModel updateViewModel)
    {
        ViewModel = updateViewModel;
    }
}
