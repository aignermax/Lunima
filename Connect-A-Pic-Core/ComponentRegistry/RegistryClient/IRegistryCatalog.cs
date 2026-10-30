namespace CAP_Core.ComponentRegistry.RegistryClient;

/// <summary>Reads registry records and artifacts without exposing HTTP or disk-cache implementation.</summary>
public interface IRegistryCatalog
{
    Task<RegistryResult<RegistryIndex>> GetIndexAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    RegistryIndex? TryGetCachedIndex();
    Task<RegistryResult<ComponentManifest>> GetComponentAsync(string manifestPath, bool forceRefresh = false,
        CancellationToken cancellationToken = default);
    Task<RegistryResult<SParameterSpectrum>> GetSpectrumAsync(string manifestPath, ArtifactRef artifact,
        bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<RegistryResult<string>> GetPreviewAsync(RegistryIndexEntry entry, bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
