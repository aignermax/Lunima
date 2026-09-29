using CAP.Avalonia.Services.ComponentRegistry;
using CAP_Core.ComponentRegistry.RegistryClient;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using UnitTests.ComponentRegistry.RegistryClient;
using Xunit;

namespace UnitTests.ComponentRegistry.RegistryDownload;

/// <summary>
/// Overwrite safety of the registry download (issue #1195): a re-download of
/// the SAME registry entry replaces its earlier copy only after backing the
/// previous PDK state up to <c>.trash</c>; a DIFFERENT registry entry with the
/// same display name is reported as a clash and writes nothing; a wavelength
/// grid finer than 1 nm is rejected instead of silently dropping samples.
/// </summary>
public class RegistryDownloadOverwriteTests : IDisposable
{
    private const string SubNanometerSpectrumJson = """
        {
          "wavelength_um": [1.55, 1.5502],
          "s": [ { "from": "o1", "to": "o2", "re": [0.7, 0.7], "im": [0.0, 0.0] } ]
        }
        """;

    private readonly RegistryTestHarness _harness = new();
    private readonly string _storeRoot = Path.Combine(
        Path.GetTempPath(), "lunima-registry-overwrite-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _harness.Dispose();
        if (Directory.Exists(_storeRoot))
            Directory.Delete(_storeRoot, recursive: true);
    }

    private UserPdkStore CreateStore() => new(_storeRoot, new PdkJsonSaver(), new PdkLoader());

    private string PdkPath =>
        CreateStore().ResolveNamedPath(RegistryDownloadService.PdkNameForProcess("generic-si220"));

    private async Task<(CAP_Core.ComponentRegistry.RegistryClient.RegistryClient Client, ComponentManifest Manifest)>
        LoadManifestAsync()
    {
        var client = _harness.CreateClient();
        var manifestResult = await client.GetComponentAsync(RegistryTestHarness.ManifestPath);
        manifestResult.IsSuccess.ShouldBeTrue();
        return (client, manifestResult.Value!);
    }

    private static Task<RegistryDownloadResult> DownloadAsync(
        RegistryDownloadService service, ComponentManifest manifest) =>
        service.DownloadAsync(
            RegistryTestHarness.ManifestPath, manifest, RegistryArtifactSelector.Select(manifest)!);

    [Fact]
    public async Task Redownload_SameRegistryId_ReplacesWithTrashBackup_InsteadOfSilentOverwrite()
    {
        var store = CreateStore();
        var (client, manifest) = await LoadManifestAsync();
        var service = new RegistryDownloadService(client, store);

        var first = await DownloadAsync(service, manifest);
        first.IsSuccess.ShouldBeTrue(first.ErrorMessage);
        first.ReplacedExisting.ShouldBeFalse();

        var second = await DownloadAsync(service, manifest);
        second.IsSuccess.ShouldBeTrue(second.ErrorMessage);
        second.ReplacedExisting.ShouldBeTrue();

        // Still exactly one component — replaced, not duplicated.
        var pdk = new PdkLoader().LoadFromFileForEditing(second.FilePath!);
        pdk.Components.ShouldHaveSingleItem().Name.ShouldBe(manifest.Name);

        // The pre-replacement state survives in .trash — no silent data loss.
        var trashDir = Path.Combine(_storeRoot, ".trash");
        Directory.Exists(trashDir).ShouldBeTrue();
        var backup = Directory.GetFiles(trashDir, "registry-generic-si220-*.json").ShouldHaveSingleItem();
        new PdkLoader().LoadFromFileForEditing(backup).Components.ShouldHaveSingleItem()
            .Name.ShouldBe(manifest.Name);
    }

    [Fact]
    public async Task Download_DifferentRegistryId_WithSameDisplayName_ReportsClash_AndWritesNothing()
    {
        var store = CreateStore();
        var (client, manifest) = await LoadManifestAsync();
        var service = new RegistryDownloadService(client, store);
        (await DownloadAsync(service, manifest)).IsSuccess.ShouldBeTrue();
        var fileTextBefore = File.ReadAllText(PdkPath);

        // A different registry entry (different id) carrying the same display name.
        var clashingResult = await client.GetComponentAsync(RegistryTestHarness.ManifestPath);
        var clashing = clashingResult.Value!;
        clashing.Id = "y-branch-1x2-fork";
        clashing.Name.ShouldBe(manifest.Name);

        var clash = await DownloadAsync(service, clashing);

        clash.IsSuccess.ShouldBeFalse();
        clash.ErrorMessage.ShouldNotBeNull();
        clash.ErrorMessage.ShouldContain(manifest.Name);
        // Neither component is touched: the file is byte-identical, no backup was made.
        File.ReadAllText(PdkPath).ShouldBe(fileTextBefore);
        new PdkLoader().LoadFromFileForEditing(PdkPath).Components.ShouldHaveSingleItem()
            .SMatrix!.SourceNote.ShouldContain("Registry: y-branch-1x2 (");
        Directory.Exists(Path.Combine(_storeRoot, ".trash")).ShouldBeFalse();
    }

    [Fact]
    public async Task Download_DifferentRegistryId_WithDifferentName_AddsAlongside()
    {
        var store = CreateStore();
        var (client, manifest) = await LoadManifestAsync();
        var service = new RegistryDownloadService(client, store);
        (await DownloadAsync(service, manifest)).IsSuccess.ShouldBeTrue();

        var otherResult = await client.GetComponentAsync(RegistryTestHarness.ManifestPath);
        var other = otherResult.Value!;
        other.Id = "mmi-2x2";
        other.Name = "MMI coupler 2x2";

        var added = await DownloadAsync(service, other);

        added.IsSuccess.ShouldBeTrue(added.ErrorMessage);
        added.ReplacedExisting.ShouldBeFalse();
        var pdk = new PdkLoader().LoadFromFileForEditing(added.FilePath!);
        pdk.Components.Select(c => c.Name).ShouldBe(["Y-branch splitter 1x2", "MMI coupler 2x2"]);
    }

    [Fact]
    public async Task Download_SubNanometerWavelengthGrid_Fails_AndWritesNothing()
    {
        var store = CreateStore();
        var (client, manifest) = await LoadManifestAsync();
        // Override the fixture spectrum with a 0.2 nm grid (sub-nm samples).
        var spectrumUrl = $"{RegistryTestHarness.BaseUrl}/" +
            CAP_Core.ComponentRegistry.RegistryClient.RegistryClient.ResolveArtifactPath(
                RegistryTestHarness.ManifestPath, RegistryTestHarness.SpectrumFile);
        _harness.Handler.AddResponse(spectrumUrl, SubNanometerSpectrumJson);
        var service = new RegistryDownloadService(client, store);

        var result = await DownloadAsync(service, manifest);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("wavelength grid finer than 1 nm is not supported");
        result.ErrorMessage.ShouldContain("1.5502");
        Directory.Exists(_storeRoot).ShouldBeFalse();
    }
}
