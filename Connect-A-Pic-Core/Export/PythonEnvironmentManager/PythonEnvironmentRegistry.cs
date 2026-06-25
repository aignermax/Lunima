using System.Text.Json;

namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>
/// Persists the list of managed Python environments and the name of the active one.
/// Environments are stored as a JSON file under the Lunima app-data directory.
/// The active environment's interpreter path can be forwarded to consumers via
/// <see cref="OnActiveEnvironmentChanged"/>.
/// </summary>
public class PythonEnvironmentRegistry
{
    private static readonly string RegistryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lunima", "python-environments.json");

    private RegistryData _data;

    /// <summary>
    /// Invoked whenever the active environment changes. The argument is the
    /// Python executable path of the new active environment, or null if none.
    /// Wire this up to <c>UserPreferencesService.SetCustomPythonPath</c> in the DI layer.
    /// </summary>
    public Action<string?>? OnActiveEnvironmentChanged { get; set; }

    /// <summary>Initialises the registry, loading any previously saved environments.</summary>
    public PythonEnvironmentRegistry()
    {
        _data = Load();
    }

    /// <summary>Returns a snapshot of all registered environments.</summary>
    public IReadOnlyList<PythonEnvironment> GetAll() =>
        _data.Environments.AsReadOnly();

    /// <summary>Returns the active environment, or null if none is set.</summary>
    public PythonEnvironment? GetActive() =>
        _data.ActiveName == null
            ? null
            : _data.Environments.FirstOrDefault(e => e.Name == _data.ActiveName);

    /// <summary>
    /// Adds a new environment or replaces an existing one with the same name,
    /// then persists the registry.
    /// </summary>
    public void AddOrUpdate(PythonEnvironment env)
    {
        var idx = _data.Environments.FindIndex(e => e.Name == env.Name);
        if (idx >= 0)
            _data.Environments[idx] = env;
        else
            _data.Environments.Add(env);
        Save();
    }

    /// <summary>
    /// Removes the environment with <paramref name="name"/> if it exists.
    /// If it was the active environment, clears the active selection.
    /// </summary>
    public void Remove(string name)
    {
        _data.Environments.RemoveAll(e => e.Name == name);
        if (_data.ActiveName == name)
            SetActive(null);
        else
            Save();
    }

    /// <summary>
    /// Sets the active environment by name. Pass null to clear the selection.
    /// Persists the change and fires <see cref="OnActiveEnvironmentChanged"/>.
    /// </summary>
    public void SetActive(string? name)
    {
        _data.ActiveName = name;
        Save();

        var pythonPath = GetActive()?.PythonExecutable;
        OnActiveEnvironmentChanged?.Invoke(pythonPath);
    }

    /// <summary>Returns true when an environment with this name already exists.</summary>
    public bool Exists(string name) =>
        _data.Environments.Any(e => e.Name == name);

    // ── Persistence ────────────────────────────────────────────────────────

    private static RegistryData Load()
    {
        if (!File.Exists(RegistryFilePath))
            return new RegistryData();

        try
        {
            var json = File.ReadAllText(RegistryFilePath);
            return JsonSerializer.Deserialize<RegistryData>(json) ?? new RegistryData();
        }
        catch
        {
            return new RegistryData();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryFilePath)!);
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RegistryFilePath, json);
    }

    // ── Data model ─────────────────────────────────────────────────────────

    private sealed class RegistryData
    {
        public List<PythonEnvironment> Environments { get; set; } = new();
        public string? ActiveName { get; set; }
    }
}
