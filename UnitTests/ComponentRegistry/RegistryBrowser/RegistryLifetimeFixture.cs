using CAP.Avalonia.Services.ComponentRegistry;
using CAP_Core.ComponentRegistry.RegistryClient;
using UnitTests.ComponentRegistry.RegistryClient;

namespace UnitTests.ComponentRegistry.RegistryBrowser;

internal sealed class RegistryLifetimeFixture(string heldStage) : IRegistryCatalog, IRegistryAdoption
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken SeenToken { get; private set; }
    internal int Calls { get; private set; }
    internal static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private async Task HoldAsync(string stage, CancellationToken token, string path = "a")
    {
        Calls++;
        if (stage != heldStage || path != "a") return;
        SeenToken = token;
        Entered.TrySetResult();
        await Release.Task; // Deliberately ignores cancellation; the owner must drain it.
    }

    public async Task<RegistryResult<RegistryIndex>> GetIndexAsync(bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await HoldAsync("index", cancellationToken);
        return RegistryResult<RegistryIndex>.Success(new()
        {
            Components = [new() { Id = "a", Name = "A", Path = "a", Process = "test",
                Preview = heldStage == "preview" ? "a.svg" : null }]
        }, RegistrySource.Network);
    }

    public RegistryIndex? TryGetCachedIndex() => null;
    public async Task<RegistryResult<ComponentManifest>> GetComponentAsync(string manifestPath,
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await HoldAsync("details", cancellationToken, manifestPath);
        return RegistryResult<ComponentManifest>.Success(new()
        {
            Id = manifestPath, Name = manifestPath, Description = manifestPath,
            Process = "test", Artifacts = new() { Simulated = [new() { File = "model.json", Status = "verified" }] }
        }, RegistrySource.Network);
    }

    public async Task<RegistryResult<string>> GetPreviewAsync(RegistryIndexEntry entry,
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await HoldAsync("preview", cancellationToken);
        return RegistryResult<string>.Success(RegistryTestHarness.ReadFixture("preview-y-branch-1x2.svg"), RegistrySource.Network);
    }

    public async Task<RegistryResult<SParameterSpectrum>> GetSpectrumAsync(string manifestPath, ArtifactRef artifact,
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await HoldAsync("spectrum", cancellationToken);
        var spectrum = System.Text.Json.JsonSerializer.Deserialize<SParameterSpectrum>(RegistryTestHarness.ReadFixture("spectrum.json"))!;
        return RegistryResult<SParameterSpectrum>.Success(spectrum, RegistrySource.Network);
    }

    public async Task<RegistryDownloadResult> DownloadAsync(string manifestPath, ComponentManifest manifest,
        RegistryArtifactChoice choice, CancellationToken cancellationToken = default)
    {
        await HoldAsync("download", cancellationToken);
        return RegistryDownloadResult.Success("Registry test", "fixture.json", false);
    }
}
