using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Components.Core;
using CAP_Core.Export;
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
}
