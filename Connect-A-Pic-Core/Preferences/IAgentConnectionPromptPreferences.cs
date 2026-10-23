namespace CAP_Core.Preferences;

/// <summary>Remembers which application version's agent-connection prompt was dismissed.</summary>
public interface IAgentConnectionPromptPreferences
{
    string? GetClaudeCodePromptDismissedVersion();
    void SetClaudeCodePromptDismissedVersion(string? version);
}
