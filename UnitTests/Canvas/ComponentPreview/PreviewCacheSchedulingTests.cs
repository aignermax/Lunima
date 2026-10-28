using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewCacheSchedulingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "preview-scheduling-" + Guid.NewGuid());

    [Fact]
    public async Task ColdLookup_ReturnsWhileEnvironmentInspectionIsBlocked()
    {
        using var release = new ManualResetEventSlim();
        var inspected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Renderer
        {
            EnvironmentRevisionResolver = () =>
            {
                inspected.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
                return "pdk-revision";
            }
        };
        var service = new GdsPreviewRenderService(backend, new GdsPreviewDiskCache(_directory));
        var key = new GdsPreviewKey("pdk", "cell", "");
        var lookup = Task.Run(() => service.TryGetGeometry(key));
        try
        {
            await inspected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            (await lookup.WaitAsync(TimeSpan.FromSeconds(1))).ShouldBeNull();
        }
        finally
        {
            release.Set();
            await lookup;
            await service.WaitForPendingAsync();
        }
    }

    [Fact]
    public async Task WarmLookup_DoesNotProbeEnvironmentForEveryComponentFrame()
    {
        var inspections = 0;
        var backend = new Renderer
        {
            EnvironmentRevisionResolver = () => { Interlocked.Increment(ref inspections); return "stable"; }
        };
        var service = new GdsPreviewRenderService(backend, new GdsPreviewDiskCache(_directory));
        var key = new GdsPreviewKey("pdk", "cell", "");
        service.TryGetGeometry(key);
        await service.WaitForPendingAsync();
        var beforeFrames = inspections;
        for (var i = 0; i < 100; i++) service.TryGetGeometry(key).ShouldNotBeNull();
        inspections.ShouldBe(beforeFrames);
    }

    private sealed class Renderer() : NazcaComponentPreviewService("unused", "missing-script.py")
    {
        public override Task<NazcaPreviewResult> RenderAsync(string? moduleName, string nazcaFunction,
            string? nazcaParameters, CancellationToken ct = default) => Task.FromResult(new NazcaPreviewResult
            {
                Success = true, XMax = 1, YMax = 1,
                Polygons = [new() { Vertices = [(0, 0), (1, 0), (0, 1)] }]
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
