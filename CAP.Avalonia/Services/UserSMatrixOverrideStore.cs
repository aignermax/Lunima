using System.Collections.ObjectModel;
using System.Text.Json;
using CAP_Core;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services;

/// <summary>
/// User-global storage for PDK-template-scoped S-matrix overrides.
/// Persists to a JSON file under the OS-standard application-data folder
/// (<c>%APPDATA%/Lunima/sparam-overrides.json</c> on Windows, <c>~/.config/Lunima/...</c>
/// on Linux). Overrides stored here apply to every project the user opens, mirroring
/// the user's mental model when they edit a PDK template's S-matrix
/// ("I just changed the 2x2 MMI Coupler — that should hold everywhere").
///
/// Per-instance overrides are NOT stored here; those remain in the project's .lun
/// file because they are tied to a specific Canvas component instance and only make
/// sense within that project.
///
/// Keys follow the same shape as the project-scoped store:
/// <c>"{pdkSource}::{templateName}"</c>, e.g. <c>"siepic-ebeam-pdk::2x2 MMI Coupler"</c>.
/// </summary>
public class UserSMatrixOverrideStore
{
    private const string FileName = "sparam-overrides.json";
    private const string AppFolderName = "Lunima";

    private readonly string _filePath;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly Dictionary<string, ComponentSMatrixData> _overrides = new();

    /// <summary>
    /// Live dictionary of user-global overrides. Mutated directly by the
    /// <see cref="ComponentSettingsDialogViewModel"/> import path (which expects
    /// a writable dictionary contract); call <see cref="Save"/> after any mutation
    /// to flush to disk. Also consumed read-only by
    /// <see cref="SMatrixOverrideApplicator.ApplyAll"/>.
    /// </summary>
    public Dictionary<string, ComponentSMatrixData> Overrides => _overrides;

    /// <summary>
    /// Absolute path to the on-disk file. Surfaced for diagnostics and for tests
    /// that need to verify persistence.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Creates a store backed by the standard application-data location.
    /// Loads existing overrides from disk if the file is present.
    /// </summary>
    public UserSMatrixOverrideStore(ErrorConsoleService? errorConsole = null)
        : this(BuildDefaultFilePath(), errorConsole)
    {
    }

    /// <summary>
    /// Test-friendly constructor that accepts an explicit file path.
    /// </summary>
    public UserSMatrixOverrideStore(string filePath, ErrorConsoleService? errorConsole = null)
    {
        _filePath = filePath;
        _errorConsole = errorConsole;
        Load();
    }

    /// <summary>
    /// Stores or replaces the override for the given template key.
    /// Does NOT auto-persist — call <see cref="Save"/> to flush to disk.
    /// </summary>
    public void Apply(string templateKey, ComponentSMatrixData data)
    {
        _overrides[templateKey] = data;
    }

    /// <summary>
    /// Removes the override for the given template key. Returns true if an
    /// entry was actually removed. Does NOT auto-persist.
    /// </summary>
    public bool Remove(string templateKey) => _overrides.Remove(templateKey);

    /// <summary>
    /// Persists the current in-memory state to <see cref="FilePath"/>.
    /// Failures are surfaced via <see cref="ErrorConsoleService"/>; the
    /// in-memory state is preserved so the caller can retry.
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_overrides, SerializerOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loud failure to the user, but in-memory state survives so the
            // session can keep working. Re-throwing would crash the dialog
            // and lose the import the user just performed.
            _errorConsole?.LogError(
                $"Could not save user-global S-matrix overrides to '{_filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reloads from disk, discarding any unsaved in-memory mutations.
    /// Useful primarily for tests; production code uses the constructor.
    /// </summary>
    public void Reload()
    {
        _overrides.Clear();
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var loaded = JsonSerializer.Deserialize<Dictionary<string, ComponentSMatrixData>>(
                json, SerializerOptions);
            if (loaded == null) return;

            foreach (var (key, value) in loaded)
                _overrides[key] = value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt file must not crash app startup. Surface it loudly so
            // the user sees that their overrides aren't being applied; the
            // store stays empty until the user fixes or deletes the file.
            _errorConsole?.LogError(
                $"Could not load user-global S-matrix overrides from '{_filePath}': {ex.Message}. " +
                "PDK template overrides will not be applied this session.", ex);
        }
    }

    private static string BuildDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, AppFolderName, FileName);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
