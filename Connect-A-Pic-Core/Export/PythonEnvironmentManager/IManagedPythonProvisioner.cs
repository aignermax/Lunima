namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>Checks and provisions the complete Python environment used by Settings.</summary>
public interface IManagedPythonProvisioner
{
    /// <summary>Live readiness check for every preview/export dependency.</summary>
    Task<bool> IsReadyAsync(PythonEnvironment environment, CancellationToken cancellationToken);
    /// <summary>Creates or repairs an owned environment; never changes interpreter selection.</summary>
    Task ProvisionAsync(PythonEnvironment environment, string version, IProgress<string> progress,
        CancellationToken cancellationToken);
}
