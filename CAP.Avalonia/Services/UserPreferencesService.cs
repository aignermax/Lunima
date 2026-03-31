using System.Text.Json;
using CAP_Core.Update;

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
    /// Test constructor that uses a custom file path.
    /// Used by unit tests to avoid polluting user preferences.
    /// </summary>
    internal UserPreferencesService(string testFilePath)
    {
        _preferencesFilePath = testFilePath;
        _preferences = LoadPreferences();
    }

    /// <summary>
    /// Gets the app data directory for Lunima.
    /// </summary>
    private static string GetAppDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "Lunima");
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

    /// <summary>
    /// Gets the saved left panel width (default 220 pixels).
    /// </summary>
    public double GetLeftPanelWidth()
    {
        return _preferences.LeftPanelWidth;
    }

    /// <summary>
    /// Sets the left panel width and saves.
    /// </summary>
    public void SetLeftPanelWidth(double width)
    {
        _preferences.LeftPanelWidth = width;
        Save();
    }

    /// <summary>
    /// Gets the saved right panel width (default 250 pixels).
    /// </summary>
    public double GetRightPanelWidth()
    {
        return _preferences.RightPanelWidth;
    }

    /// <summary>
    /// Sets the right panel width and saves.
    /// </summary>
    public void SetRightPanelWidth(double width)
    {
        _preferences.RightPanelWidth = width;
        Save();
    }

    /// <summary>
    /// Gets the custom Python path, or null if using system default.
    /// </summary>
    public string? GetCustomPythonPath()
    {
        return _preferences.CustomPythonPath;
    }

    /// <summary>
    /// Sets the custom Python path and saves.
    /// </summary>
    public void SetCustomPythonPath(string? pythonPath)
    {
        _preferences.CustomPythonPath = pythonPath;
        Save();
    }

    /// <summary>
    /// Gets the version the user chose to skip during update prompts.
    /// Returns null if no version was skipped.
    /// </summary>
    public SemanticVersion? GetSkippedUpdateVersion()
    {
        return SemanticVersion.TryParse(_preferences.SkippedUpdateVersion, out var v) ? v : null;
    }

    /// <summary>
    /// Stores the version the user chose to skip and saves preferences.
    /// </summary>
    public void SetSkippedUpdateVersion(SemanticVersion version)
    {
        _preferences.SkippedUpdateVersion = version.ToString();
        Save();
    }

    /// <summary>
    /// Clears any skipped update version and saves preferences.
    /// </summary>
    public void ClearSkippedUpdateVersion()
    {
        _preferences.SkippedUpdateVersion = null;
        Save();
    }

    /// <summary>
    /// Records that the user chose "Skip for Today", suppressing the startup notification
    /// until the next calendar day.
    /// </summary>
    public void SkipToday()
    {
        _preferences.SkipTodayDate = DateTime.UtcNow.Date;
        Save();
    }

    /// <summary>
    /// Returns <c>true</c> when the startup update check should run today.
    /// Returns <c>false</c> if the user already clicked "Skip for Today" today.
    /// </summary>
    public bool ShouldCheckToday()
    {
        return _preferences.SkipTodayDate == null
            || _preferences.SkipTodayDate.Value.Date < DateTime.UtcNow.Date;
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

    /// <summary>
    /// Width of the left panel in pixels (default 220).
    /// </summary>
    public double LeftPanelWidth { get; set; } = 220;

    /// <summary>
    /// Width of the right panel in pixels (default 250).
    /// </summary>
    public double RightPanelWidth { get; set; } = 250;

    /// <summary>
    /// Custom Python executable path for Nazca/GDS export.
    /// If null, system default (python3/python) will be used.
    /// </summary>
    public string? CustomPythonPath { get; set; }

    /// <summary>
    /// Version string the user chose to skip during update prompts (e.g. "1.2.3").
    /// Null means no version is skipped.
    /// </summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>
    /// UTC date when the user last chose "Skip for Today".
    /// Null means never skipped. Reset daily.
    /// </summary>
    public DateTime? SkipTodayDate { get; set; }
}
