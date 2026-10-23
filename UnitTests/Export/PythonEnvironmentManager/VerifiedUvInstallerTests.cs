using System.IO.Compression;
using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class VerifiedUvInstallerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lunima-uv-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidArchive_InstallsOnlyBinary_AndReusesVerifiedCache()
    {
        var bytes = Archive("uv.exe", "trusted binary");
        using var handler = new ArchiveHandler(bytes);
        using var client = new HttpClient(handler);
        var installer = new VerifiedUvInstaller(client, _directory);
        var spec = Artifact(bytes);
        var path = await installer.EnsureAsync(spec, null, CancellationToken.None);
        (await File.ReadAllTextAsync(path)).ShouldBe("trusted binary");
        (await installer.EnsureAsync(spec, null, CancellationToken.None)).ShouldBe(path);
        handler.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task AlteredDownload_IsRejectedBeforeExecutableIsInstalled()
    {
        var expected = Archive("uv.exe", "trusted");
        using var handler = new ArchiveHandler(Archive("uv.exe", "tampered"));
        using var client = new HttpClient(handler);
        var installer = new VerifiedUvInstaller(client, _directory);
        await Should.ThrowAsync<InvalidDataException>(() => installer.EnsureAsync(Artifact(expected), null, CancellationToken.None));
        Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task TamperedInstalledBinary_IsRestoredFromVerifiedArchive()
    {
        var bytes = Archive("uv.exe", "trusted");
        using var handler = new ArchiveHandler(bytes);
        using var client = new HttpClient(handler);
        var installer = new VerifiedUvInstaller(client, _directory);
        var spec = Artifact(bytes);
        var path = await installer.EnsureAsync(spec, null, CancellationToken.None);
        await File.WriteAllTextAsync(path, "tampered");
        await installer.EnsureAsync(spec, null, CancellationToken.None);
        (await File.ReadAllTextAsync(path)).ShouldBe("trusted");
        handler.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task ArchiveWithoutExpectedBinary_FailsAndCleansStagingFiles()
    {
        var bytes = Archive("other.exe", "unexpected");
        using var handler = new ArchiveHandler(bytes);
        using var client = new HttpClient(handler);
        var installer = new VerifiedUvInstaller(client, _directory);
        await Should.ThrowAsync<InvalidDataException>(() => installer.EnsureAsync(Artifact(bytes), null, CancellationToken.None));
        Directory.EnumerateFiles(_directory, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
        Directory.EnumerateFiles(_directory, "uv.exe", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task ConcurrentSetup_UsesOneDownloadAndACompleteBinary()
    {
        var bytes = Archive("uv.exe", "trusted");
        using var handler = new ArchiveHandler(bytes);
        using var client = new HttpClient(handler);
        var installer = new VerifiedUvInstaller(client, _directory);
        var secondInstaller = new VerifiedUvInstaller(client, _directory);
        var spec = Artifact(bytes);
        var paths = await Task.WhenAll(installer.EnsureAsync(spec, null, CancellationToken.None),
            secondInstaller.EnsureAsync(spec, null, CancellationToken.None));
        paths[0].ShouldBe(paths[1]);
        (await File.ReadAllTextAsync(paths[0])).ShouldBe("trusted");
        handler.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task CorruptCachedArchive_IsDownloadedAgainInsteadOfTrusted()
    {
        var bytes = Archive("uv.exe", "trusted");
        using var handler = new ArchiveHandler(bytes);
        using var client = new HttpClient(handler);
        var installer = new VerifiedUvInstaller(client, _directory);
        var spec = Artifact(bytes);
        var binary = await installer.EnsureAsync(spec, null, CancellationToken.None);
        var archive = Directory.GetFiles(_directory, "*.zip", SearchOption.AllDirectories).Single();
        await File.WriteAllBytesAsync(archive, Archive("uv.exe", "poisoned cache"));
        await installer.EnsureAsync(spec, null, CancellationToken.None);
        handler.Requests.ShouldBe(2);
        (await File.ReadAllTextAsync(binary)).ShouldBe("trusted");
    }

    [Fact]
    public async Task CanceledSetup_DoesNotCreateExecutableOrStagingFile()
    {
        var bytes = Archive("uv.exe", "trusted");
        using var handler = new ArchiveHandler(bytes);
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            new VerifiedUvInstaller(client, _directory).EnsureAsync(Artifact(bytes), null, cancellation.Token));
        Directory.Exists(_directory).ShouldBeFalse();
        handler.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task TarArchive_ExtractsRegularExecutable_WithoutImportingDirectoryTree()
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("trusted unix binary"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "uv-platform/uv") { DataStream = content });
        }
        var bytes = memory.ToArray();
        using var handler = new ArchiveHandler(bytes);
        using var client = new HttpClient(handler);
        var spec = new UvReleaseArtifact("test", "unix-platform", "uv-test.tar.gz",
            Convert.ToHexString(SHA256.HashData(bytes)), "uv");
        var path = await new VerifiedUvInstaller(client, _directory).EnsureAsync(spec, null, CancellationToken.None);
        (await File.ReadAllTextAsync(path)).ShouldBe("trusted unix binary");
        Directory.GetDirectories(_directory, "uv-platform", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private static UvReleaseArtifact Artifact(byte[] bytes) => new(
        "test", "test-platform", "uv-test.zip", Convert.ToHexString(SHA256.HashData(bytes)), "uv.exe");

    private static byte[] Archive(string entryName, string content)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("bundle/" + entryName).Open();
            entry.Write(Encoding.UTF8.GetBytes(content));
        }
        return memory.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }
    }
}
