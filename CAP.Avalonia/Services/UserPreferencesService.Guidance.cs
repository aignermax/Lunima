namespace CAP.Avalonia.Services;

public partial class UserPreferencesService
{
    public IReadOnlyList<int> GetCompletedLearningChapters() =>
        (_preferences.CompletedLearningChapters ?? new()).Where(n => n is >= 1 and <= 7).Distinct().ToArray();

    public int GetLastLearningChapter() => Math.Clamp(_preferences.LastLearningChapter, 1, 7);

    public void SetLearningProgress(IEnumerable<int> completed, int current)
    {
        _preferences.CompletedLearningChapters = completed.Where(n => n is >= 1 and <= 7).Distinct().Order().ToList();
        _preferences.LastLearningChapter = Math.Clamp(current, 1, 7);
        Save();
    }
    public bool GetGuidedLearningEnabled() => _preferences.GuidedLearningEnabled;

    public void SetGuidedLearningEnabled(bool enabled)
    {
        _preferences.GuidedLearningEnabled = enabled;
        Save();
    }
}
