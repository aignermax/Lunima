namespace CAP_Core.Preferences;

/// <summary>Local agent server admission, endpoint and authentication settings.</summary>
public interface IAgentBridgePreferences
{
    bool GetAgentBridgeEnabled();
    void SetAgentBridgeEnabled(bool enabled);
    int GetAgentBridgePort();
    void SetAgentBridgePort(int port);
    bool GetAgentBridgeRequireToken();
    void SetAgentBridgeRequireToken(bool required);
    string GetOrCreateAgentBridgeToken();
    string RegenerateAgentBridgeToken();
    string? GetRequiredAgentBridgeToken();
}
