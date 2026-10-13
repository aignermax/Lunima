using CAP.Avalonia.Services.Localization;

namespace CAP.Avalonia.ViewModels.Settings.AgentBridge;

/// <summary>
/// Settings page "Agent bridge (MCP)" — configures the local MCP server that lets an
/// external AI agent such as Claude Code work on the open design.
/// </summary>
public sealed class AgentBridgeSettingsPage : LocalizedSettingsPage, IDisposable
{
    private readonly AgentBridgeSettingsViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public AgentBridgeSettingsPage(AgentBridgeSettingsViewModel viewModel, LocalizationService localization)
        : base("Settings.Section.AgentBridge", localization)
    {
        _viewModel = viewModel;
    }

    /// <inheritdoc/>
    public override string Icon => "Icon.Terminal";

    /// <inheritdoc/>
    public override object ViewModel => _viewModel;

    /// <summary>Releases the language and server-status subscriptions.</summary>
    public new void Dispose()
    {
        base.Dispose();
        _viewModel.Dispose();
    }
}
