using CAP_Core.Export.PythonEnvironmentManager;

namespace CAP_Core.Export.Contracts;

/// <summary>Probes the selected environment and updates its package/version health information.</summary>
public interface IPythonEnvironmentHealth
{
    Task<PythonEnvironment> CheckAsync(PythonEnvironment environment, CancellationToken cancellationToken = default);
}
