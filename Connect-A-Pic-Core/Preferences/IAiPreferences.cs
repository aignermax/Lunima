namespace CAP_Core.Preferences;

/// <summary>Credential used by the in-application AI assistant.</summary>
public interface IAiPreferences
{
    string GetAiApiKey();
    void SetAiApiKey(string apiKey);
}
