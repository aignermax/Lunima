using System.Security.Cryptography;

namespace CAP.Avalonia.Services;

/// <summary>
/// Agent-bridge (local MCP server) preference access for <see cref="UserPreferencesService"/>:
/// the opt-in toggle, the loopback port, the optional per-install bearer token and the
/// "Connect Claude Code" prompt state.
/// </summary>
public partial class UserPreferencesService
{
    /// <summary>Port the agent bridge listens on when the user has not chosen one.</summary>
    public const int DefaultAgentBridgePort = 39271;

    /// <summary>Random bytes in a generated agent-bridge token (256 bit).</summary>
    private const int AgentBridgeTokenBytes = 32;

    /// <summary>Whether the agent bridge server is enabled. False until the user opts in.</summary>
    public bool GetAgentBridgeEnabled() => _preferences.AgentBridgeEnabled;

    /// <summary>Persists the agent bridge opt-in toggle.</summary>
    public void SetAgentBridgeEnabled(bool enabled)
    {
        _preferences.AgentBridgeEnabled = enabled;
        Save();
    }

    /// <summary>The configured agent bridge port, or <see cref="DefaultAgentBridgePort"/> when unset/invalid.</summary>
    public int GetAgentBridgePort()
    {
        var port = _preferences.AgentBridgePort;
        return port is > 0 and <= ushort.MaxValue ? port : DefaultAgentBridgePort;
    }

    /// <summary>Persists the agent bridge port.</summary>
    public void SetAgentBridgePort(int port)
    {
        _preferences.AgentBridgePort = port;
        Save();
    }

    /// <summary>
    /// The per-install bearer token the agent bridge requires. Generated (and persisted)
    /// on first access so every installation has its own secret.
    /// </summary>
    public string GetOrCreateAgentBridgeToken()
    {
        if (string.IsNullOrWhiteSpace(_preferences.AgentBridgeToken))
            return RegenerateAgentBridgeToken();

        return _preferences.AgentBridgeToken;
    }

    /// <summary>
    /// Replaces the agent bridge token with a fresh random one (invalidating every
    /// previously configured agent) and persists it.
    /// </summary>
    public string RegenerateAgentBridgeToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(AgentBridgeTokenBytes);
        _preferences.AgentBridgeToken = Convert.ToHexString(bytes).ToLowerInvariant();
        Save();
        return _preferences.AgentBridgeToken;
    }

    /// <summary>Whether requests must carry the bearer token. False by default.</summary>
    public bool GetAgentBridgeRequireToken() => _preferences.AgentBridgeRequireToken;

    /// <summary>Persists the "require access token" toggle.</summary>
    public void SetAgentBridgeRequireToken(bool required)
    {
        _preferences.AgentBridgeRequireToken = required;
        Save();
    }

    /// <summary>
    /// The token every request must present, or null when the token is not required
    /// (the default). Used by the endpoint and by the connect command.
    /// </summary>
    public string? GetRequiredAgentBridgeToken() =>
        GetAgentBridgeRequireToken() ? GetOrCreateAgentBridgeToken() : null;

    /// <summary>App version for which the "Connect Claude Code" prompt was dismissed, or null.</summary>
    public string? GetClaudeCodePromptDismissedVersion() => _preferences.ClaudeCodePromptDismissedVersion;

    /// <summary>Remembers that the user dismissed the "Connect Claude Code" prompt for <paramref name="version"/>.</summary>
    public void SetClaudeCodePromptDismissedVersion(string? version)
    {
        _preferences.ClaudeCodePromptDismissedVersion = version;
        Save();
    }
}
