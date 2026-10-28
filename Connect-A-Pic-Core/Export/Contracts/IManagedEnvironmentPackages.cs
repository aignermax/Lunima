using CAP_Core.Export.PythonEnvironmentManager;

namespace CAP_Core.Export.Contracts;

/// <summary>Installs requested capabilities in a managed environment without choosing the active interpreter.</summary>
public interface IManagedEnvironmentPackages
{
    Task<PythonEnvironment> EnsureGdsFactoryAsync(PythonEnvironment? environment, IProgress<string> progress,
        CancellationToken cancellationToken, CancellationToken callerCancellation = default, CancellationToken stopping = default);
    Task<PythonEnvironment> EnsureBackendAsync(PythonEnvironment? environment, string packageSpec,
        IProgress<string> progress, CancellationToken cancellationToken,
        CancellationToken callerCancellation = default, CancellationToken stopping = default);
}
