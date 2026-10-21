using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Rendering;

public class GroupFrozenPathRasterCacheTests
{
    [AvaloniaFact]
    public void NearDensityBoundary_UsesBoundedImage_ButDoesNotBlurAtCloseZoom()
    {
        var group = Group();
        group.InternalPaths[^1].TranslateBy(10000, 0);
        var cache = new GroupFrozenPathRasterCache();
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 100));
        using var context = bitmap.CreateDrawingContext();
        cache.TryDraw(context, cache.Prepare(group), 0.254, null).ShouldBeFalse();
        cache.TryDraw(context, cache.Prepare(group), 0.254, null).ShouldBeTrue(
            "a .25-density image is only magnified 1.016 times and avoids a vector cliff");
        cache.TryDraw(context, cache.Prepare(group), 0.5, null).ShouldBeFalse(
            "doubling that image would blur geometry, so close zoom keeps the vector fallback");
    }

    [AvaloniaFact]
    public void StableImportedGroup_UsesOneRaster_AndPreservesOverlappingPaintOrder()
    {
        var group = Group();
        var cache = new GroupFrozenPathRasterCache();
        var snapshot = cache.Prepare(group);
        // Warm each source as in the normal main-canvas path before comparing.
        using var warm = new RenderTargetBitmap(new PixelSize(256, 100));
        using (var ctx = warm.CreateDrawingContext())
            foreach (var source in snapshot.Sources) AsDrawnGeometryRenderer.Draw(ctx, source!, 0.5, null);
        using var aggregate = new RenderTargetBitmap(new PixelSize(256, 100));
        using (var ctx = aggregate.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(0.5, 0.5)))
        {
            cache.TryDraw(ctx, snapshot, 0.5, null).ShouldBeFalse();
            cache.TryDraw(ctx, cache.Prepare(group), 0.5, null).ShouldBeTrue();
        }
        using var sequential = new RenderTargetBitmap(new PixelSize(256, 100));
        using (var ctx = sequential.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(0.5, 0.5)))
            foreach (var source in snapshot.Sources) AsDrawnGeometryRenderer.Draw(ctx, source!, 0.5, null);
        Pixel(aggregate, 20, 20).ShouldBe(Pixel(sequential, 20, 20));
        Pixel(aggregate, 20, 20).ShouldNotBe(0u);
    }

    [AvaloniaFact]
    public void ChangedSourceAndVisibility_DoNotReuseOldPixels()
    {
        var group = Group(singleLayer: true);
        var cache = new GroupFrozenPathRasterCache();
        Draw(cache, group, null).Dispose();
        using (var before = Draw(cache, group, null)) Pixel(before, 20, 20).ShouldNotBe(0u);
        foreach (var path in group.InternalPaths) path.TranslateBy(200, 0);
        Draw(cache, group, null).Dispose();
        using (var moved = Draw(cache, group, null))
        {
            Pixel(moved, 20, 20).ShouldBe(0u);
            Pixel(moved, 120, 20).ShouldNotBe(0u);
        }
        var hidden = new GdsLayerVisibilityState();
        hidden.Set(1, 0, false, 1);
        Draw(cache, group, hidden).Dispose();
        using var filtered = Draw(cache, group, hidden);
        Pixel(filtered, 120, 20).ShouldBe(0u);
    }

    [AvaloniaFact]
    public void MixedSourceAndEditedRoutes_FallBackToOrderedIndividualDrawing()
    {
        var group = Group();
        group.InternalPaths[10].AsDrawnGeometry = null;
        var cache = new GroupFrozenPathRasterCache();
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 100));
        using var ctx = bitmap.CreateDrawingContext();
        cache.TryDraw(ctx, cache.Prepare(group), 0.5, null).ShouldBeFalse();
        cache.TryDraw(ctx, cache.Prepare(group), 0.5, null).ShouldBeFalse();
    }

    private static RenderTargetBitmap Draw(GroupFrozenPathRasterCache cache, ComponentGroup group, GdsLayerVisibilityState? visibility)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(256, 100));
        using var ctx = bitmap.CreateDrawingContext();
        using var transform = ctx.PushTransform(Matrix.CreateScale(0.5, 0.5));
        var snapshot = cache.Prepare(group);
        if (!cache.TryDraw(ctx, snapshot, 0.5, visibility))
            foreach (var source in snapshot.Sources) AsDrawnGeometryRenderer.Draw(ctx, source!, 0.5, visibility);
        return bitmap;
    }

    private static ComponentGroup Group(bool singleLayer = false)
    {
        var group = new ComponentGroup("import");
        for (int i = 0; i < 32; i++)
        {
            var routed = new RoutedPath();
            routed.Segments.Add(new StraightSegment(20, 20, 80, 20, 0));
            var path = new FrozenWaveguidePath { Path = routed };
            path.AsDrawnGeometry = new AsDrawnGeometry(new[] { new OutlinePolygon
            {
                Layer = singleLayer ? 1 : i % 2 + 1,
                Points = new[] { new OutlinePoint(20, 20), new OutlinePoint(80, 20),
                    new OutlinePoint(80, 80), new OutlinePoint(20, 80), new OutlinePoint(20, 20) }
            }});
            group.InternalPaths.Add(path);
        }
        return group;
    }

    private static uint Pixel(RenderTargetBitmap bitmap, int x, int y)
    {
        var pointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), pointer, 4, 4);
            return unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(pointer));
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer); }
    }
}
