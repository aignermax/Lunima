using System.Text.Json;
using CAP.Avalonia.Services.ComponentRegistry;
using CAP_Core.ComponentRegistry.RegistryClient;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using UnitTests.ComponentRegistry.RegistryBrowser;
using UnitTests.ComponentRegistry.RegistryClient;

namespace UnitTests.ComponentRegistry.RegistryDownload;

public sealed class RegistryAdoptionCancellationTests
{
    [Fact]
    public async Task CancelledFetch_CannotWriteOrRegisterLateArtifact()
    {
        var directory = Path.Combine(Path.GetTempPath(), "registry-cancel-" + Guid.NewGuid().ToString("N"));
        var catalog = new RegistryLifetimeFixture("spectrum");
        var store = new UserPdkStore(directory, new PdkJsonSaver(), new PdkLoader());
        var registrations = new List<string>();
        var adoption = new RegistryDownloadService(catalog, store, registrations.Add);
        var manifest = JsonSerializer.Deserialize<ComponentManifest>(RegistryTestHarness.ReadFixture("component.json"))!;
        using var cancellation = new CancellationTokenSource();
        var pending = adoption.DownloadAsync("a", manifest, RegistryArtifactSelector.Select(manifest)!, cancellation.Token);
        try
        {
            await catalog.Entered.Task.WaitAsync(RegistryLifetimeFixture.Guard);
            cancellation.Cancel();
            catalog.Release.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => pending.WaitAsync(RegistryLifetimeFixture.Guard));
            registrations.ShouldBeEmpty();
            if (Directory.Exists(directory)) Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        }
        finally
        {
            catalog.Release.TrySetResult();
            try { await pending.WaitAsync(RegistryLifetimeFixture.Guard); }
            catch (OperationCanceledException) { }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
