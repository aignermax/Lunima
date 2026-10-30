using CAP_Core.Export.PythonEnvironmentManager;

namespace CAP_Core.Export.Contracts;

/// <summary>Catalog and interpreter selection for managed Python environments.</summary>
public interface IPythonEnvironmentRegistry
{
    IReadOnlyList<PythonEnvironment> GetAll();
    PythonEnvironment? GetActive();
    bool Exists(string name);
    void AddOrUpdate(PythonEnvironment environment);
    void Remove(string name);
    void SetActive(string? name);
    void SetActiveExternalPath(string pythonPath);
}
