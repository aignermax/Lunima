namespace CAP_Core.Preferences;

/// <summary>Persisted startup choice owned by the Home workflow.</summary>
public interface IHomePreferences
{
    bool GetReopenLastProjectOnStartup();
    void SetReopenLastProjectOnStartup(bool enabled);
}
