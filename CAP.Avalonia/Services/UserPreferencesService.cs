using System.Text.Json;

namespace CAP.Avalonia.Services;

/// <summary>
/// Manages user preferences with JSON file persistence.
/// Stores PDK filter states and other user settings.
/// </summary>
public class UserPreferencesService
{
    private readonly string _preferencesFilePath;
    private UserPreferences _preferences;

    public UserPreferencesService()
    {
        var appDataDir = GetAppDataDirectory();
        Directory.CreateDirectory(appDataDir);
        _preferencesFilePath = Path.Combine(appDataDir, "user-preferences.json");
        _preferences = LoadPreferences();
    }

    /// <summary>
    /// Gets the app data directory for Connect-A-PIC Pro.
    /// </summary>
    private static string GetAppDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "Connect-A-PIC-Pro");
    }

    /// <summary>
    /// Loads preferences from disk or creates defaults.
    /// </summary>
    private UserPreferences LoadPreferences()
    {
        try
        {
            if (File.Exists(_preferencesFilePath))
            {
                var json = File.ReadAllText(_preferencesFilePath);
                var prefs = JsonSerializer.Deserialize<UserPreferences>(json);
                return prefs ?? new UserPreferences();
            }
        }
        catch
        {
            // If load fails, use defaults
        }

        return new UserPreferences();
    }

    /// <summary>
    /// Saves current preferences to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_preferences, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_preferencesFilePath, json);
        }
        catch
        {
            // Fail silently - preferences are not critical
        }
    }

    /// <summary>
    /// Gets the list of enabled PDK names.
    /// </summary>
    public HashSet<string> GetEnabledPdks()
    {
        return new HashSet<string>(_preferences.EnabledPdks, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets the list of enabled PDK names and saves.
    /// </summary>
    public void SetEnabledPdks(IEnumerable<string> pdkNames)
    {
        _preferences.EnabledPdks = pdkNames.ToList();
        Save();
    }

    /// <summary>
    /// Gets the list of user-loaded PDK file paths for auto-reload.
    /// </summary>
    public List<string> GetUserPdkPaths()
    {
        return new List<string>(_preferences.UserPdkPaths);
    }

    /// <summary>
    /// Adds a user-loaded PDK path and saves.
    /// </summary>
    public void AddUserPdkPath(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (!_preferences.UserPdkPaths.Contains(normalizedPath))
        {
            _preferences.UserPdkPaths.Add(normalizedPath);
            Save();
        }
    }

    /// <summary>
    /// Removes a user-loaded PDK path and saves.
    /// </summary>
    public void RemoveUserPdkPath(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (_preferences.UserPdkPaths.Remove(normalizedPath))
        {
            Save();
        }
    }
}

/// <summary>
/// User preferences data structure.
/// </summary>
public class UserPreferences
{
    /// <summary>
    /// List of PDK names that are currently enabled (visible in library).
    /// </summary>
    public List<string> EnabledPdks { get; set; } = new();

    /// <summary>
    /// List of user-loaded PDK file paths to auto-load at startup.
    /// </summary>
    public List<string> UserPdkPaths { get; set; } = new();
}
