namespace CAP_Core.Preferences;

/// <summary>Persisted interface language, including the system-language choice.</summary>
public interface ILanguagePreferences
{
    string GetUiLanguage();
    void SetUiLanguage(string languageCodeOrSystem);
}
