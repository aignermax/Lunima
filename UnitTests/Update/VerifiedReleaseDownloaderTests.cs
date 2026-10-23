using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CAP_Core.Update;
using CAP_DataAccess.Updates;
using Shouldly;

namespace UnitTests.Update;

public sealed class VerifiedReleaseDownloaderTests : IDisposable
{
    private readonly string _staging = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lunima-verified-update-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] TrustedBytes = Encoding.UTF8.GetBytes("published artifact");

    [Fact]
    public async Task ValidDownload_IsOwnedUntilDisposed()
    {
        using var client = Client(TrustedBytes);
        var result = await new VerifiedReleaseDownloader(client, _staging)
            .DownloadAsync(new Uri("https://example.test/update.msi"), Artifact());
        var path = result.Path;
        File.ReadAllBytes(path).ShouldBe(TrustedBytes);
        result.Artifact.Sha256.ShouldBe(Convert.ToHexString(SHA256.HashData(TrustedBytes)).ToLowerInvariant());
        result.Dispose();
        File.Exists(path).ShouldBeFalse();
        Directory.EnumerateFileSystemEntries(_staging).ShouldBeEmpty();
    }

    [Fact]
    public async Task RepeatedDisposal_DoesNotDeleteAReplacementAtTheRetiredPath()
    {
        using var client = Client(TrustedBytes);
        var result = await new VerifiedReleaseDownloader(client, _staging)
            .DownloadAsync(new Uri("https://example.test/update.msi"), Artifact());
        result.Dispose();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(result.Path)!);
        await File.WriteAllTextAsync(result.Path, "new owner");
        result.Dispose();
        File.Exists(result.Path).ShouldBeTrue("the old handle released its ownership on the first Dispose");
        (await File.ReadAllTextAsync(result.Path)).ShouldBe("new owner");
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    public async Task BytesNotMatchingSignedIdentity_AreRejectedAndRemoved(string failure)
    {
        var bytes = failure switch
        {
            "truncated" => TrustedBytes[..^1],
            "oversized" => TrustedBytes.Concat(new byte[] { 42 }).ToArray(),
            _ => TrustedBytes.Select(b => (byte)(b ^ 1)).ToArray()
        };
        using var client = Client(bytes);
        await Should.ThrowAsync<InvalidDataException>(() => new VerifiedReleaseDownloader(client, _staging)
            .DownloadAsync(new Uri("https://example.test/update.msi"), Artifact()));
        if (Directory.Exists(_staging)) Directory.EnumerateFileSystemEntries(_staging).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownLengthBody_IsStillCheckedAgainstSignedSize(bool oversized)
    {
        var bytes = oversized ? TrustedBytes.Concat(new byte[] { 42 }).ToArray() : TrustedBytes[..^1];
        using var client = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnseekableBytes(bytes))
        }));
        await Should.ThrowAsync<InvalidDataException>(() => new VerifiedReleaseDownloader(client, _staging)
            .DownloadAsync(new Uri("https://example.test/update.msi"), Artifact()));
        Directory.EnumerateFileSystemEntries(_staging).ShouldBeEmpty();
    }

    private sealed class UnseekableBytes(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public async Task CanceledDownload_DoesNotLeaveATemporaryArtifact()
    {
        using var client = Client(TrustedBytes);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => new VerifiedReleaseDownloader(client, _staging)
            .DownloadAsync(new Uri("https://example.test/update.msi"), Artifact(), cancellationToken: stop.Token));
        Directory.Exists(_staging).ShouldBeFalse();
    }

    [Fact]
    public async Task HttpFailure_DoesNotLeaveATemporaryArtifact()
    {
        using var client = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Should.ThrowAsync<HttpRequestException>(() => new VerifiedReleaseDownloader(client, _staging)
            .DownloadAsync(new Uri("https://example.test/update.msi"), Artifact()));
        if (Directory.Exists(_staging)) Directory.EnumerateFileSystemEntries(_staging).ShouldBeEmpty();
    }

    [Fact]
    public async Task HttpClientHeaderTimeout_IsReportedAsTimeoutRatherThanUserCancellation()
    {
        using var client = new HttpClient(new StalledHeaders()) { Timeout = TimeSpan.FromMilliseconds(100) };
        await Should.ThrowAsync<TimeoutException>(() => new VerifiedReleaseDownloader(client, _staging)
            .DownloadAsync(new Uri("https://example.test/update.msi"), Artifact()));
        Directory.Exists(_staging).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedBody_IsBoundedAndCleaned(bool callerCanceled)
    {
        using var stream = new StalledStream();
        using var client = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        using var stop = new CancellationTokenSource();
        var download = new VerifiedReleaseDownloader(client, _staging,
            callerCanceled ? TimeSpan.FromMinutes(1) : TimeSpan.FromMilliseconds(100));
        Task<VerifiedUpdateDownload>? work = null;
        try
        {
            work = download.DownloadAsync(new Uri("https://example.test/update.msi"), Artifact(), cancellationToken: stop.Token);
            await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (callerCanceled) stop.Cancel();
            (await Task.WhenAny(work, Task.Delay(1500))).ShouldBeSameAs(work);
            if (callerCanceled) await Should.ThrowAsync<OperationCanceledException>(() => work);
            else await Should.ThrowAsync<TimeoutException>(() => work);
            Directory.EnumerateFileSystemEntries(_staging).ShouldBeEmpty();
        }
        finally
        {
            stop.Cancel();
            if (work != null) try { (await work).Dispose(); } catch (Exception) { }
        }
    }

    private static HttpClient Client(byte[] bytes) => new(new Handler(() =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));

    private static VerifiedReleaseArtifact Artifact()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, version = "2.0.0", sourceCommit = new string('a', 40),
            assets = new[] { new { name = "update.msi", runtimeIdentifier = "win-x64", size = TrustedBytes.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(TrustedBytes)) } }
        });
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, keyId = "fixture", payload = Convert.ToBase64String(payload),
            signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        });
        return new ReleaseManifestVerifier(new Dictionary<string, string> { ["fixture"] = key.ExportSubjectPublicKeyInfoPem() })
            .Verify(envelope, "2.0.0", new SemanticVersion(1, 0, 0), "win-x64", "update.msi");
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }

    private sealed class StalledStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StalledHeaders : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The header wait must be canceled.");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_staging)) Directory.Delete(_staging, recursive: true);
    }
}
