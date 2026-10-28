namespace CAP_Core.Preferences;

/// <summary>Persisted chapter progress and opt-in guidance for the learning workflow.</summary>
public interface ILearningPreferences
{
    IReadOnlyList<int> GetCompletedLearningChapters();
    int GetLastLearningChapter();
    void SetLearningProgress(IEnumerable<int> completed, int current);
    bool GetGuidedLearningEnabled();
    void SetGuidedLearningEnabled(bool enabled);
}
