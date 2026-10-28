using CAP_Core.ComponentRegistry.RegistryClient;

namespace CAP.Avalonia.Services.ComponentRegistry;

/// <summary>Adopts one reviewed artifact into the user's component library.</summary>
public interface IRegistryAdoption
{
    Task<RegistryDownloadResult> DownloadAsync(string manifestPath, ComponentManifest manifest,
        RegistryArtifactChoice choice, CancellationToken cancellationToken = default);
}
