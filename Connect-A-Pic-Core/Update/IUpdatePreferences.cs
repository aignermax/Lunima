namespace CAP_Core.Update;

/// <summary>Only the persisted choices needed by the update workflow.</summary>
public interface IUpdatePreferences
{
    SemanticVersion? GetSkippedUpdateVersion();
    void SetSkippedUpdateVersion(SemanticVersion version);
    bool ShouldCheckToday();
    void SkipToday();
}
