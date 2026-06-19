using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using Moq;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>Unit tests for <see cref="GdsPreviewRenderService"/>.</summary>
public sealed class GdsPreviewRenderServiceTests
{
    // ── BuildCacheKey ───────────────────────────────────────────────────────

    [Fact]
    public void BuildCacheKey_ComponentWithNazcaFunction_ReturnsKeyWithFunctionAndDimensions()
    {
        var comp = TestComponentFactory.CreateComponentViewModel(
            nazcaFunctionName: "demo.mmi1x2_sh");

        var key = GdsPreviewRenderService.BuildCacheKey(comp);

        key.ShouldNotBeNull();
        key!.ShouldStartWith("demo.mmi1x2_sh|");
    }

    [Fact]
    public void BuildCacheKey_ComponentWithEmptyNazcaFunction_ReturnsNull()
    {
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "");
        GdsPreviewRenderService.BuildCacheKey(comp).ShouldBeNull();
    }

    [Fact]
    public void BuildCacheKey_ComponentWithNullNazcaFunction_ReturnsNull()
    {
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: null);
        GdsPreviewRenderService.BuildCacheKey(comp).ShouldBeNull();
    }

    [Fact]
    public void BuildCacheKey_DifferentDimensions_ReturnsDifferentKeys()
    {
        // Components with same function but different sizes should have different keys
        var comp1 = TestComponentFactory.CreateComponentViewModel(
            nazcaFunctionName: "demo.io", widthMicrometers: 4, heightMicrometers: 4);
        var comp2 = TestComponentFactory.CreateComponentViewModel(
            nazcaFunctionName: "demo.io", widthMicrometers: 8, heightMicrometers: 4);

        var key1 = GdsPreviewRenderService.BuildCacheKey(comp1);
        var key2 = GdsPreviewRenderService.BuildCacheKey(comp2);

        key1.ShouldNotBe(key2);
    }

    // ── TryGetPreview — fallback behaviour ─────────────────────────────────

    [Fact]
    public void TryGetPreview_ComponentWithoutNazcaFunction_ReturnsNull()
    {
        var service = new GdsPreviewRenderService(
            new NazcaComponentPreviewService("python3", "/nonexistent/script.py"));

        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "");

        // Should return null immediately (no fetch triggered)
        service.TryGetPreview(comp).ShouldBeNull();
    }

    [Fact]
    public void TryGetPreview_FirstCallWithNazcaFunction_ReturnsNullWhileFetching()
    {
        var service = new GdsPreviewRenderService(
            new NazcaComponentPreviewService("python3", "/nonexistent/script.py"));

        var comp = TestComponentFactory.CreateComponentViewModel(
            nazcaFunctionName: "demo.mmi1x2_sh");

        // First call enqueues fetch and returns null (fetch not yet complete)
        var result = service.TryGetPreview(comp);
        result.ShouldBeNull();
    }

    // ── TryGetGeometry — key-based lookup with disk cache + render throttle ──

    private static NazcaPreviewResult Ok() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = 4, YMax = 2,
        Polygons = new List<NazcaPreviewPolygon>
        {
            new() { Layer = 1, Vertices = new List<(double, double)> { (0, 0), (4, 0), (4, 2) } }
        }
    };

    [Fact]
    public async Task GetGeometry_RendersOnce_ThenServesFromMemory()
    {
        var mock = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null);
        mock.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());

        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-svc-" + Guid.NewGuid().ToString("N"));
        var svc = new GdsPreviewRenderService(mock.Object, new GdsPreviewDiskCache(diskDir));
        var key = new GdsPreviewKey("m", "f", "p");

        svc.TryGetGeometry(key).ShouldBeNull();      // miss → async render kicked off
        await svc.WaitForPendingAsync();
        svc.TryGetGeometry(key).ShouldNotBeNull();   // now in memory
        mock.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        try { Directory.Delete(diskDir, true); } catch { }
    }

    [Fact]
    public async Task GetGeometry_SecondInstance_ServesFromDisk_NoRender()
    {
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-svc-" + Guid.NewGuid().ToString("N"));
        var key = new GdsPreviewKey("m", "f", "p");

        var mock1 = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null);
        mock1.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());
        var svc1 = new GdsPreviewRenderService(mock1.Object, new GdsPreviewDiskCache(diskDir));
        svc1.TryGetGeometry(key);
        await svc1.WaitForPendingAsync();   // populates disk

        var mock2 = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null);
        var svc2 = new GdsPreviewRenderService(mock2.Object, new GdsPreviewDiskCache(diskDir));
        svc2.TryGetGeometry(key);
        await svc2.WaitForPendingAsync();
        svc2.TryGetGeometry(key).ShouldNotBeNull();
        mock2.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        try { Directory.Delete(diskDir, true); } catch { }
    }
}
