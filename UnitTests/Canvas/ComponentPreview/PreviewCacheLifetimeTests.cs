using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewCacheLifetimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "preview-lifetime-" + Guid.NewGuid());

    [Fact]
    public void GeometryCache_EvictsByPayloadBeforeEntryLimit()
    {
        var cache = new GdsGeometryCache();
        cache.Set("first", Geometry(new string('a', 9 * 1024 * 1024)));
        cache.Set("second", Geometry(new string('b', 9 * 1024 * 1024)));
        cache.TryGet("first", out _).ShouldBeFalse();
        cache.TryGet("second", out _).ShouldBeTrue();
    }

    [Fact]
    public void GeometryCache_DoesNotRetainOversizedCoordinates()
    {
        var cache = new GdsGeometryCache();
        cache.Set("small", Geometry());
        cache.Set("large", new NazcaPreviewResult
        {
            Success = true,
            Polygons = new[] { new NazcaPreviewPolygon { Vertices = new (double, double)[2_100_000] } }
        });
        cache.TryGet("large", out _).ShouldBeFalse();
        cache.TryGet("small", out _).ShouldBeTrue();
    }

    [Fact]
    public void GeometryCache_RecentHitSurvivesEntryPressure()
    {
        var cache = new GdsGeometryCache();
        for (var i = 0; i < 200; i++) cache.Set(i.ToString(), Geometry());
        cache.TryGet("0", out _).ShouldBeTrue();
        cache.Set("new", Geometry());
        cache.TryGet("0", out _).ShouldBeTrue();
        cache.TryGet("1", out _).ShouldBeFalse();
        cache.Count.ShouldBe(200);
    }

    [Fact]
    public void BitmapCache_CanServeExternallyOwnedOversizedGeometry()
    {
        var cache = new GdsPreviewCache();
        var geometry = Geometry(new string('x', 17 * 1024 * 1024));
        cache.Set("large", new GdsPreviewData(geometry, 1, 1));
        cache.TryGet("large", out var preview).ShouldBeTrue();
        preview!.Result.ShouldBeSameAs(geometry);
        GC.KeepAlive(geometry);
    }

    [AvaloniaFact]
    public async Task QueuedRasterization_IsDiscardedAfterActivation()
    {
        var service = new GdsPreviewRenderService(new ControlledRenderer(), new GdsPreviewDiskCache(_directory));
        var component = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "pdk.cell").Component;
        service.TryGetPreview(component);
        await service.WaitForPendingAsync();
        service.TryGetPreview(component).ShouldNotBeNull();
        service.RetryFailedPreviews();
        var published = 0;
        service.OnPreviewLoaded += () => published++;
        Dispatcher.UIThread.RunJobs();
        published.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Activation_InvalidatesDiskEvenWhenRestartedBeforeNewRender()
    {
        var key = new GdsPreviewKey("pdk", "cell", "");
        var original = new GdsPreviewRenderService(new ControlledRenderer(), new GdsPreviewDiskCache(_directory));
        original.TryGetGeometry(key);
        await original.WaitForPendingAsync();
        original.RetryFailedPreviews();
        var next = new GdsPreviewRenderService(new ControlledRenderer { Result = Geometry(width: 2) },
            new GdsPreviewDiskCache(_directory));
        next.TryGetGeometry(key);
        await next.WaitForPendingAsync();
        next.TryGetGeometry(key)!.XMax.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task Activation_InvalidatesSuccessfulMemoryAndDiskGeometry()
    {
        var backend = new ControlledRenderer();
        var service = new GdsPreviewRenderService(backend, new GdsPreviewDiskCache(_directory));
        var key = new GdsPreviewKey("pdk", "cell", "");
        service.TryGetGeometry(key);
        await service.WaitForPendingAsync();
        service.TryGetGeometry(key)!.XMax.ShouldBe(1);

        backend.Result = Geometry(width: 2);
        service.RetryFailedPreviews();
        service.TryGetGeometry(key).ShouldBeNull();
        await service.WaitForPendingAsync();
        service.TryGetGeometry(key)!.XMax.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task OldInflightSuccess_CannotRepopulateAfterActivation()
    {
        var backend = new ControlledRenderer();
        var delayed = new TaskCompletionSource<NazcaPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = delayed.Task;
        var service = new GdsPreviewRenderService(backend, new GdsPreviewDiskCache(_directory));
        var key = new GdsPreviewKey("pdk", "cell", "");
        service.TryGetGeometry(key);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.RetryFailedPreviews();
        backend.Pending = null;
        backend.Result = Geometry(width: 2);
        delayed.SetResult(Geometry(width: 1));
        await service.WaitForPendingAsync();

        service.TryGetGeometry(key).ShouldBeNull();
        await service.WaitForPendingAsync();
        service.TryGetGeometry(key)!.XMax.ShouldBe(2);
    }

    [Fact]
    public async Task InlineCodeEdit_WithRevisionInvalidation_DoesNotReusePreviousGeometry()
    {
        var backend = new ControlledRenderer();
        var code = "first";
        var service = new GdsPreviewRenderService(backend, new GdsPreviewDiskCache(_directory))
        {
            InlineNazcaCodeLookup = (_, _) => code
        };
        var key = new GdsPreviewKey("pdk", "cell", "");
        service.TryGetGeometry(key);
        await service.WaitForPendingAsync();
        service.TryGetGeometry(key)!.XMax.ShouldBe(1);
        code = "edited";
        backend.Result = Geometry(width: 2);
        service.InvalidatePdkPreviews();
        service.TryGetGeometry(key).ShouldBeNull();
        await service.WaitForPendingAsync();
        service.TryGetGeometry(key)!.XMax.ShouldBe(2);
    }

    private static NazcaPreviewResult Geometry(string? source = null, double width = 1) => new()
    {
        Success = true, XMax = width, YMax = 1, Source = source,
        Polygons = new[] { new NazcaPreviewPolygon { Vertices = new[] { (0d, 0d), (width, 0d), (0d, 1d) } } }
    };

    private sealed class ControlledRenderer() : NazcaComponentPreviewService("unused", "missing-script.py")
    {
        public NazcaPreviewResult Result { get; set; } = Geometry();
        public Task<NazcaPreviewResult>? Pending { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<NazcaPreviewResult> RenderAsync(string? moduleName, string nazcaFunction,
            string? nazcaParameters, CancellationToken ct = default)
        {
            var result = Pending ?? Task.FromResult(Result);
            Started.TrySetResult();
            return result;
        }
        public override Task<NazcaPreviewResult> RenderRawCodeAsync(string code, CancellationToken ct = default)
            => Pending ?? Task.FromResult(Result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
