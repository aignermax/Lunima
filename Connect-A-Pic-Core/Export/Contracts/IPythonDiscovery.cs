namespace CAP_Core.Export.Contracts;

/// <summary>Discovers and probes Python installations available for layout generation.</summary>
public interface IPythonDiscovery
{
    Task<List<PythonInstallation>> DiscoverPythonWithNazcaAsync(CancellationToken cancellationToken = default);
    Task<string?> FindFirstNazcaPythonPathAsync(CancellationToken cancellationToken = default);
    Task<PythonInstallation?> CheckPythonInstallation(string pythonPath, string source,
        bool probeGdsFactory = false, CancellationToken cancellationToken = default);
}
