using CAP.Avalonia.ViewModels.AI;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for AI Design Assistant configuration (API key and model selection).
/// Shares the singleton <see cref="AiAssistantViewModel"/> so the key entered here
/// is immediately available to the chat panel in the right sidebar.
/// </summary>
public class AiAssistantSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "AI Assistant";

    /// <inheritdoc/>
    public string Icon => "🤖";

    /// <inheritdoc/>
    public string? Category => null;

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AiAssistantSettingsPage"/>.
    /// </summary>
    public AiAssistantSettingsPage(AiAssistantViewModel aiAssistantViewModel)
    {
        ViewModel = aiAssistantViewModel;
    }
}
