namespace CAP_Core.Preferences;

/// <summary>Persisted library visibility and user-added PDK paths.</summary>
public interface IPdkLibraryPreferences
{
    HashSet<string> GetEnabledPdks();
    HashSet<string> GetKnownPdks();
    void SetPdkFilterState(IEnumerable<string> enabledPdkNames, IEnumerable<string> knownPdkNames);
    List<string> GetUserPdkPaths();
    void AddUserPdkPath(string filePath);
    void RemoveUserPdkPath(string filePath);
}
