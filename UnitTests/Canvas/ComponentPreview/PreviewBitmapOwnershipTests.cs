using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewBitmapOwnershipTests
{
    [Fact]
    public void ClearingGeometryOwner_ReleasesGeometryDespiteRasterCacheEntry()
    {
        var owner = new PreviewGeometryCache();
        var cache = new GdsPreviewCache();
        var geometry = Populate(owner, cache);
        owner.Clear();
        Collect();
        geometry.TryGetTarget(out _).ShouldBeFalse();
        cache.TryGet("preview", out _).ShouldBeFalse();
    }

    [Fact]
    public void CollectedWrapper_IsReconstructedWhileGeometryLives()
    {
        var geometry = new NazcaPreviewResult { Success = true };
        var cache = new GdsPreviewCache();
        var wrapper = PopulateWrapper(cache, geometry);
        Collect();
        wrapper.TryGetTarget(out _).ShouldBeFalse();
        cache.TryGet("preview", out var recovered).ShouldBeTrue();
        recovered!.Result.ShouldBeSameAs(geometry);
        recovered.WidthMicrometers.ShouldBe(10);
        recovered.HeightMicrometers.ShouldBe(20);
        GC.KeepAlive(geometry);
    }

    [AvaloniaFact]
    public void BitmapPixels_RespectSeparateBudgetAndRejectOversizedRaster()
    {
        using var firstBitmap = new RenderTargetBitmap(new PixelSize(8, 8));
        using var secondBitmap = new RenderTargetBitmap(new PixelSize(8, 8));
        var first = new GdsPreviewData(new NazcaPreviewResult { Success = true }, 1, 1) { Bitmap = firstBitmap };
        var second = new GdsPreviewData(new NazcaPreviewResult { Success = true }, 1, 1) { Bitmap = secondBitmap };
        var cache = new GdsPreviewCache(maxBitmapBytes: 300);
        cache.Set("first", first);
        cache.Set("second", second);
        cache.TryGet("first", out _).ShouldBeFalse();
        cache.TryGet("second", out var retained).ShouldBeTrue();
        retained.ShouldBeSameAs(second);
        var smaller = new GdsPreviewCache(maxBitmapBytes: 200);
        smaller.Set("oversized", first);
        smaller.TryGet("oversized", out _).ShouldBeFalse();
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NazcaPreviewResult> Populate(PreviewGeometryCache owner, GdsPreviewCache cache)
    {
        var result = new NazcaPreviewResult { Success = true };
        owner.Set("raw", result);
        cache.Set("preview", new GdsPreviewData(result, 10, 20));
        return new WeakReference<NazcaPreviewResult>(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<GdsPreviewData> PopulateWrapper(GdsPreviewCache cache, NazcaPreviewResult result)
    {
        var wrapper = new GdsPreviewData(result, 10, 20);
        cache.Set("preview", wrapper);
        return new WeakReference<GdsPreviewData>(wrapper);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
