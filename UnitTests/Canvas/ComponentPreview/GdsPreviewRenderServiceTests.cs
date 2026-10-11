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
    // ── Render key of a placed component ───────────────────────────────────

    [Fact]
    public void ForComponent_NazcaComponent_KeysOnModuleFunctionAndParameters()
    {
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "demo.mmi1x2_sh").Component;

        var key = GdsPreviewKey.ForComponent(comp);

        key.Function.ShouldBe("demo.mmi1x2_sh");
        key.Parameters.ShouldBe(comp.NazcaFunctionParameters);
        key.IsRenderable.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ForComponent_WithoutNazcaFunction_IsNotRenderable(string? function)
    {
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: function).Component;
        GdsPreviewKey.ForComponent(comp).IsRenderable.ShouldBeFalse();
    }

    [Fact]
    public void ForComponent_DifferentParameters_GetDifferentKeys()
    {
        var a = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "demo.io").Component;
        var b = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "demo.io").Component;
        a.NazcaFunctionParameters = "length=10";
        b.NazcaFunctionParameters = "length=20";

        GdsPreviewKey.ForComponent(a).Hash().ShouldNotBe(GdsPreviewKey.ForComponent(b).Hash());
    }

    [Fact]
    public void ForComponent_MatchesLibraryThumbnailKey_SoTheRenderIsShared()
    {
        // The library thumbnail keys a template on (module, function, template parameters);
        // a placed instance carries the same strings, so both resolve to one cached render.
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "demo.mmi1x2_sh").Component;
        comp.NazcaModuleName = "demo";
        comp.NazcaFunctionParameters = "";
        var thumbnailKey = new GdsPreviewKey("demo", "demo.mmi1x2_sh", null);

        GdsPreviewKey.ForComponent(comp).Hash().ShouldBe(thumbnailKey.Hash());
    }

    [Fact]
    public void ForComponent_GdsFactoryNative_IgnoresSynthesizedNazcaName()
    {
        // Placement gives a gdsfactory-native component a synthesized nazcaFunction
        // ("nazca_<name>") no Nazca script can render; the gdsfactory factory wins.
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "nazca_mmi1x2").Component;
        comp.GdsFactoryFunction = "cspdk.sin300.mmi1x2";

        var key = GdsPreviewKey.ForComponent(comp);

        key.Function.ShouldBeNull();
        key.GdsFactoryFunction.ShouldBe("cspdk.sin300.mmi1x2");
        key.IsRenderable.ShouldBeTrue();
    }

    [Fact]
    public void BuildPreviewKey_RotatedFootprint_MatchesUnrotated()
    {
        // The bitmap holds unrotated geometry, so the bitmap key uses the unrotated size;
        // a rotation must not trigger a second rasterisation.
        var key = new GdsPreviewKey("m", "f", null);
        var (w, h) = GdsPolygonRenderer.GetUnrotatedSize(90, 4, 8);

        GdsPreviewRenderService.BuildPreviewKey(key, w, h)
            .ShouldBe(GdsPreviewRenderService.BuildPreviewKey(key, 8, 4));
    }

    [Fact]
    public async Task GetGeometry_GdsFactoryKey_RendersViaGdsFactoryServiceNotNazca()
    {
        // A gdsfactory-native render identity must be resolved by the gdsfactory preview
        // back-end (RenderRawCodeAsync with generated get_component code), never Nazca (#570).
        var nazca = new Mock<NazcaComponentPreviewService>("python", "nazca.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        // The gdsfactory back-end is typed as the base service (mockable, sealed derived type isn't).
        var gf = new Mock<NazcaComponentPreviewService>("python", "gf.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        gf.Setup(s => s.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());

        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-gf-" + Guid.NewGuid().ToString("N"));
        var svc = new GdsPreviewRenderService(nazca.Object, new GdsPreviewDiskCache(diskDir), gf.Object);
        var key = new GdsPreviewKey(null, null, null) { GdsFactoryFunction = "cspdk.sin300.mmi1x2" };

        key.IsRenderable.ShouldBeTrue();
        svc.TryGetGeometry(key).ShouldBeNull();       // miss → async render kicked off
        await svc.WaitForPendingAsync();
        svc.TryGetGeometry(key).ShouldNotBeNull();    // rendered via gdsfactory service

        gf.Verify(s => s.RenderRawCodeAsync(
            It.Is<string>(c => c.Contains("gf.get_component('mmi1x2')")), It.IsAny<CancellationToken>()), Times.Once);
        nazca.Verify(s => s.RenderAsync(
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        try { Directory.Delete(diskDir, true); } catch { }
    }

    // ── TryGetPreview — fallback behaviour ─────────────────────────────────

    [Fact]
    public void TryGetPreview_ComponentWithoutNazcaFunction_ReturnsNull()
    {
        var service = new GdsPreviewRenderService(
            new NazcaComponentPreviewService("python3", "/nonexistent/script.py"));

        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "");

        // Should return null immediately (no fetch triggered)
        service.TryGetPreview(comp.Component).ShouldBeNull();
    }

    [Fact]
    public void TryGetPreview_FirstCallWithNazcaFunction_ReturnsNullWhileFetching()
    {
        var service = new GdsPreviewRenderService(
            new NazcaComponentPreviewService("python3", "/nonexistent/script.py"));

        var comp = TestComponentFactory.CreateComponentViewModel(
            nazcaFunctionName: "demo.mmi1x2_sh");

        // First call enqueues fetch and returns null (fetch not yet complete)
        var result = service.TryGetPreview(comp.Component);
        result.ShouldBeNull();
    }

    // ── TryGetPreview — failure caching + render throttle ──────────────────

    [Fact]
    public async Task TryGetPreview_FailingRender_IsFetchedOnlyOncePerSession()
    {
        // A synthesized import function name ("nazca_<cell>") fails every render;
        // the failure must be remembered so the Python subprocess is spawned at
        // most once per key, not once per frame.
        var mock = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        mock.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NazcaPreviewResult.Fail("unknown function"));
        var svc = new GdsPreviewRenderService(mock.Object);
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "nazca_imported_cell");

        svc.TryGetPreview(comp.Component).ShouldBeNull();
        await svc.WaitForPendingAsync();
        svc.TryGetPreview(comp.Component).ShouldBeNull();
        svc.TryGetPreview(comp.Component).ShouldBeNull();
        await svc.WaitForPendingAsync();

        mock.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryGetPreview_FailureMarker_SurvivesLruEviction()
    {
        // A large import carries more unique failing keys than the LRU geometry cache
        // holds; the failure markers live outside the LRU so an evicted key must not
        // re-spawn a render.
        var mock = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        mock.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NazcaPreviewResult.Fail("unknown function"));
        var svc = new GdsPreviewRenderService(mock.Object);
        var first = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "nazca_cell_first");

        svc.TryGetPreview(first.Component);
        await svc.WaitForPendingAsync();
        for (int i = 0; i < GdsGeometryCache.MaxEntries + 10; i++)
            svc.TryGetPreview(TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: $"nazca_cell_{i}").Component);
        await svc.WaitForPendingAsync();

        svc.TryGetPreview(first.Component).ShouldBeNull();
        await svc.WaitForPendingAsync();
        mock.Verify(s => s.RenderAsync(It.IsAny<string?>(), "nazca_cell_first", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryGetPreview_ManyUniqueKeys_RendersAtMostThreeConcurrently()
    {
        var tracker = new object();
        int active = 0, maxActive = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mock = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        mock.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string? _, string _, string? _, CancellationToken _) =>
            {
                lock (tracker) { active++; maxActive = Math.Max(maxActive, active); }
                await release.Task;
                lock (tracker) { active--; }
                return NazcaPreviewResult.Fail("blocked");
            });
        var svc = new GdsPreviewRenderService(mock.Object);

        for (int i = 0; i < 10; i++)
            svc.TryGetPreview(TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: $"nazca_gate_{i}").Component);
        release.SetResult();
        await svc.WaitForPendingAsync();

        maxActive.ShouldBeLessThanOrEqualTo(3);
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
        var mock = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
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
    public async Task GetGeometry_RenderFails_IsNotPersisted_AndRetriesOnNextInstance()
    {
        // A failed render (broken/half-provisioned interpreter) must NOT be persisted as an
        // empty marker — otherwise the component stays blank forever, even after the env is
        // fixed. A fresh instance must re-attempt the render.
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-fail-" + Guid.NewGuid().ToString("N"));
        var key = new GdsPreviewKey("m", "f", "p");

        var failing = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        failing.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NazcaPreviewResult.Fail("interpreter not ready"));
        var svc1 = new GdsPreviewRenderService(failing.Object, new GdsPreviewDiskCache(diskDir));
        svc1.TryGetGeometry(key);
        await svc1.WaitForPendingAsync();

        // A new instance (e.g. after the env is fixed) must render, not serve a persisted "empty".
        var ok = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        ok.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());
        var svc2 = new GdsPreviewRenderService(ok.Object, new GdsPreviewDiskCache(diskDir));
        svc2.TryGetGeometry(key);
        await svc2.WaitForPendingAsync();

        svc2.TryGetGeometry(key).ShouldNotBeNull();
        ok.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        try { Directory.Delete(diskDir, true); } catch { }
    }

    [Fact]
    public async Task GetGeometry_GenuinelyEmptyRender_PersistsEmpty_NoRetry()
    {
        // A successful render with 0 polygons is genuinely empty (not a failure) — persist it so
        // a second instance does not pointlessly re-render nothing.
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-empty-" + Guid.NewGuid().ToString("N"));
        var key = new GdsPreviewKey("m", "f", "p");

        var empty = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        empty.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NazcaPreviewResult { Success = true, Polygons = new List<NazcaPreviewPolygon>() });
        var svc1 = new GdsPreviewRenderService(empty.Object, new GdsPreviewDiskCache(diskDir));
        svc1.TryGetGeometry(key);
        await svc1.WaitForPendingAsync();

        var mock2 = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        var svc2 = new GdsPreviewRenderService(mock2.Object, new GdsPreviewDiskCache(diskDir));
        svc2.TryGetGeometry(key);
        await svc2.WaitForPendingAsync();
        mock2.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        try { Directory.Delete(diskDir, true); } catch { }
    }

    [Fact]
    public async Task GetGeometry_SecondInstance_ServesFromDisk_NoRender()
    {
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-svc-" + Guid.NewGuid().ToString("N"));
        var key = new GdsPreviewKey("m", "f", "p");

        var mock1 = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        mock1.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());
        var svc1 = new GdsPreviewRenderService(mock1.Object, new GdsPreviewDiskCache(diskDir));
        svc1.TryGetGeometry(key);
        await svc1.WaitForPendingAsync();   // populates disk

        var mock2 = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        var svc2 = new GdsPreviewRenderService(mock2.Object, new GdsPreviewDiskCache(diskDir));
        svc2.TryGetGeometry(key);
        await svc2.WaitForPendingAsync();
        svc2.TryGetGeometry(key).ShouldNotBeNull();
        mock2.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        try { Directory.Delete(diskDir, true); } catch { }
    }

    // ── Canvas preview shares the library geometry cache ───────────────────

    [Fact]
    public async Task TryGetPreview_AfterLibraryThumbnailRendered_ReusesGeometryWithoutSecondRender()
    {
        var mock = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        mock.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-svc-" + Guid.NewGuid().ToString("N"));
        var svc = new GdsPreviewRenderService(mock.Object, new GdsPreviewDiskCache(diskDir));
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "demo.mmi").Component;
        comp.NazcaModuleName = "demo";
        comp.NazcaFunctionParameters = "";

        svc.TryGetGeometry(new GdsPreviewKey("demo", "demo.mmi", null));   // library thumbnail
        await svc.WaitForPendingAsync();
        var preview = svc.TryGetPreview(comp);                              // component placed

        preview.ShouldNotBeNull();
        mock.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        try { Directory.Delete(diskDir, true); } catch { }
    }

    [Fact]
    public async Task TryGetPreview_SecondInstance_ServesFromDisk_NoRender()
    {
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-svc-" + Guid.NewGuid().ToString("N"));
        var first = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        first.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());
        var comp = TestComponentFactory.CreateComponentViewModel(nazcaFunctionName: "demo.mmi").Component;
        var svc1 = new GdsPreviewRenderService(first.Object, new GdsPreviewDiskCache(diskDir));
        svc1.TryGetPreview(comp);
        await svc1.WaitForPendingAsync();

        // A restarted app: fresh service, same disk cache, no Python render.
        var second = new Mock<NazcaComponentPreviewService>("python", "script.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        var svc2 = new GdsPreviewRenderService(second.Object, new GdsPreviewDiskCache(diskDir));
        svc2.TryGetPreview(comp);
        await svc2.WaitForPendingAsync();

        svc2.TryGetPreview(comp).ShouldNotBeNull();
        second.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        try { Directory.Delete(diskDir, true); } catch { }
    }
}
