namespace CAP_Core.Preferences;

/// <summary>The selected interpreter and its activation, including same-path repairs.</summary>
public interface IPythonInterpreterPreferences
{
    string? GetCustomPythonPath();
    void SetCustomPythonPath(string? pythonPath);
    event Action? PythonInterpreterActivated;
}
