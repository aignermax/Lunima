using System.Net;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class UvDownloadLifetimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lunima-uv-body-" + Guid.NewGuid().ToString("N"));
    private static readonly UvReleaseArtifact Artifact = new("test", "platform", "uv.zip", new string('0', 64), "uv.exe");

    [Fact]
    public async Task StalledBody_IsBoundedAfterHeaders_AndCleansPartialDownload()
    {
        using var stream = new StalledStream();
        using var client = new HttpClient(new HeadersOnlyHandler(stream));
        using var cancellation = new CancellationTokenSource();
        var work = new VerifiedUvInstaller(client, _directory, TimeSpan.FromMilliseconds(50))
            .EnsureAsync(Artifact, null, cancellation.Token);
        try
        {
            await stream.Reading.Task.WaitAsync(TimeSpan.FromSeconds(2));
            (await Task.WhenAny(work, Task.Delay(1000))).ShouldBeSameAs(work,
                "the download deadline must cover body reads, after headers have arrived");
            await Should.ThrowAsync<TimeoutException>(() => work);
            Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        }
        finally
        {
            cancellation.Cancel();
            try { await work; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task CallerCancellationDuringBody_IsPreservedAndCleansPartialDownload()
    {
        using var stream = new StalledStream();
        using var client = new HttpClient(new HeadersOnlyHandler(stream));
        using var cancellation = new CancellationTokenSource();
        var work = new VerifiedUvInstaller(client, _directory).EnsureAsync(Artifact, null, cancellation.Token);
        try
        {
            await stream.Reading.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(1)));
            Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        }
        finally
        {
            cancellation.Cancel();
            try { await work; } catch (OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class HeadersOnlyHandler(Stream body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
    }

    private sealed class StalledStream : Stream
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
