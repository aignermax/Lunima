using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using CAP.Avalonia.Services.Lifetime;
using Moq;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewServiceRetirementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_CancelsAndDrainsRendererBeforeCacheDirectoryCanBeRemoved(bool applicationRetirement)
    {
        var path = TemporaryDirectory();
        var barrier = new RetirementBarrier();
        var backend = Backend();
        backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string? _, string _, string? _, CancellationToken ct) =>
            { await barrier.WaitAsync(ct); return Success(); });
        var application = new ApplicationWorkCoordinator();
        var previews = new GdsPreviewRenderService(backend.Object, new GdsPreviewDiskCache(path), applicationWork: application);
        var key = new GdsPreviewKey("test", "active", null);
        previews.TryGetGeometry(key);
        var running = previews.WaitForPendingAsync();
        var stopping = Task.CompletedTask;
        try
        {
            await barrier.AwaitStartAsync(running);
            stopping = applicationRetirement ? application.StopAsync() : previews.StopAsync();
            await barrier.AssertDrainingAsync(stopping);
        }
        finally
        {
            await barrier.FinishAsync(running, stopping);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        previews.TryGetGeometry(key).ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_RejectsColdAndWarmLookups(bool warm)
    {
        var path = TemporaryDirectory();
        var backend = Backend();
        var previews = new GdsPreviewRenderService(backend.Object, new GdsPreviewDiskCache(path));
        var key = new GdsPreviewKey("test", "terminal", null);
        try
        {
            if (warm)
            {
                previews.TryGetGeometry(key);
                await previews.WaitForPendingAsync().WaitAsync(TimeSpan.FromSeconds(5));
                previews.TryGetGeometry(key).ShouldNotBeNull();
            }
            backend.Invocations.Clear();
            await previews.StopAsync();
            previews.TryGetGeometry(key).ShouldBeNull();
            await previews.WaitForPendingAsync().WaitAsync(TimeSpan.FromSeconds(5));
            backend.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            await previews.WaitForPendingAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task Retirement_AbandonsQueuedRendersInsteadOfStartingAnotherProcess()
    {
        var path = TemporaryDirectory();
        var release = Signal();
        var firstThree = Signal();
        var calls = 0;
        var backend = Backend();
        backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string? _, string _, string? _, CancellationToken ct) =>
            {
                if (Interlocked.Increment(ref calls) == 3) firstThree.TrySetResult();
                await release.Task;
                return Success();
            });
        var previews = new GdsPreviewRenderService(backend.Object, new GdsPreviewDiskCache(path));
        for (var i = 0; i < 4; i++) previews.TryGetGeometry(new("test", "queued" + i, null));
        var stopping = Task.CompletedTask;
        try
        {
            await firstThree.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping = previews.StopAsync();
            release.TrySetResult();
            await Task.WhenAll(stopping, previews.WaitForPendingAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            calls.ShouldBe(3, "retirement must close queued render admission as well as cancel active renderers");
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(stopping, previews.WaitForPendingAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private static Mock<NazcaComponentPreviewService> Backend()
    {
        var backend = new Mock<NazcaComponentPreviewService>("python", "script", null, null);
        backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success());
        return backend;
    }

    private static NazcaPreviewResult Success() => new()
    {
        Success = true, Polygons = [new() { Vertices = [(0, 0), (4, 0), (4, 2)] }],
    };
    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "preview-retire-" + Guid.NewGuid());
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
