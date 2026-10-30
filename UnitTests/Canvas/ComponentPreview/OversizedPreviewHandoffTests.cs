using System.Runtime.CompilerServices;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class OversizedPreviewHandoffTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "preview-oversized-" + Guid.NewGuid());
    private readonly GdsPreviewKey _key = new("pdk", "cell", "");

    [Fact]
    public async Task OversizedResult_IsAvailableWhileCallerOwnsIt_WithoutRepeatedRendering()
    {
        var backend = new Renderer();
        var service = new GdsPreviewRenderService(backend, new GdsPreviewDiskCache(_directory));
        try
        {
            service.TryGetGeometry(_key);
            await service.WaitForPendingAsync();
            for (var i = 0; i < 5; i++)
            {
                service.TryGetGeometry(_key).ShouldBeSameAs(backend.Result);
                await service.WaitForPendingAsync();
            }
            backend.Calls.ShouldBe(1);
        }
        finally { await service.WaitForPendingAsync(); }
    }

    [Fact]
    public async Task CollectedOversizedResult_UsesStableFallbackWithoutRenderingAgain()
    {
        var backend = new Renderer();
        var service = new GdsPreviewRenderService(backend, new GdsPreviewDiskCache(_directory));
        try
        {
            service.TryGetGeometry(_key);
            await service.WaitForPendingAsync();
            var weak = Capture(service, _key);
            backend.Result = new NazcaPreviewResult { Success = true };
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            weak.TryGetTarget(out _).ShouldBeFalse();
            for (var i = 0; i < 5; i++)
            {
                service.TryGetGeometry(_key).ShouldBeNull();
                await service.WaitForPendingAsync();
            }
            backend.Calls.ShouldBe(1);
        }
        finally { await service.WaitForPendingAsync(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NazcaPreviewResult> Capture(GdsPreviewRenderService service, GdsPreviewKey key)
    {
        var geometry = service.TryGetGeometry(key);
        geometry.ShouldNotBeNull();
        return new WeakReference<NazcaPreviewResult>(geometry);
    }

    private sealed class Renderer() : NazcaComponentPreviewService("unused", "missing-script.py")
    {
        public int Calls { get; private set; }
        public NazcaPreviewResult Result { get; set; } = new()
        {
            Success = true, XMax = 1, YMax = 1, Source = new string('x', 17 * 1024 * 1024),
            Polygons = [new() { Vertices = [(0, 0), (1, 0), (0, 1)] }]
        };
        public override Task<NazcaPreviewResult> RenderAsync(string? moduleName, string nazcaFunction,
            string? nazcaParameters, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
