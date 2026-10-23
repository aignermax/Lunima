using CAP.Avalonia.Services.AgentBridge.Contracts;
using CAP_Core.Preferences;
using CAP.Avalonia.Services.AgentBridge.Hosting;

namespace CAP.Avalonia.Services.AgentBridge.Registration;

/// <summary>
/// The one-click "Connect Claude Code" action shared by the settings page and the startup
/// prompt: turns the bridge on (a registered but stopped bridge would only give Claude
/// "connection refused"), then registers it with Claude Code for the configured port and,
/// if the user requires one, the access token.
/// </summary>
public sealed class ClaudeCodeConnector : IAgentConnection
{
    private readonly IAgentBridgePreferences _preferences;
    private readonly IAgentBridgeControl _host;

    /// <summary>Creates the connector.</summary>
    public ClaudeCodeConnector(IAgentBridgePreferences preferences, IAgentBridgeControl host, ClaudeCodeRegistrar registrar)
    {
        _preferences = preferences;
        _host = host;
        Registrar = registrar;
    }

    /// <summary>The CLI wrapper (exposed for the "is it registered yet?" startup check).</summary>
    public ClaudeCodeRegistrar Registrar { get; }

    /// <summary>The command a user can paste into a terminal when the CLI is not found.</summary>
    public string ManualCommand =>
        AgentBridgeHost.ClaudeCodeConnectCommand(_preferences.GetAgentBridgePort(), _preferences.GetRequiredAgentBridgeToken());

    public Task<bool?> IsRegisteredAsync(CancellationToken cancellationToken = default) =>
        Registrar.IsRegisteredAsync(cancellationToken);

    /// <summary>Enables and starts the bridge, then registers it with Claude Code.</summary>
    public async Task<ClaudeRegistrationResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        _preferences.SetAgentBridgeEnabled(true);
        await _host.ApplySettingsAsync();
        return await Registrar.RegisterAsync(
            _preferences.GetAgentBridgePort(), _preferences.GetRequiredAgentBridgeToken(), cancellationToken);
    }
}
