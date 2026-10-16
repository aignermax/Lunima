namespace CAP.Avalonia.Services;

public partial class UserPreferencesService
{
    public bool GetGuidedLearningEnabled() => _preferences.GuidedLearningEnabled;

    public void SetGuidedLearningEnabled(bool enabled)
    {
        _preferences.GuidedLearningEnabled = enabled;
        Save();
    }
}
