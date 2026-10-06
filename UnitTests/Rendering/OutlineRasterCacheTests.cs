using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.Rendering;

/// <summary>
/// Zoomed out, outline geometry is drawn from a cached bitmap (one image per component)
/// instead of thousands of vector polygons; zoomed in it stays vector.
/// </summary>
public class OutlineRasterCacheTests
{
    private static readonly OutlinePolygon[] ManyPolygons = Enumerable.Range(0, 400)
        .Select(i => new OutlinePolygon
        {
            Layer = 1,
            Points = new[]
            {
                new OutlinePoint(i * 10, 0), new OutlinePoint(i * 10 + 8, 0),
                new OutlinePoint(i * 10 + 8, 50), new OutlinePoint(i * 10, 50), new OutlinePoint(i * 10, 0),
            },
        })
        .ToArray();

    [AvaloniaFact]
    public void ZoomedOut_SecondFrameDrawsOneImageInsteadOfThePolygons_AndPaintsThem()
    {
        var renderer = new ComponentOutlineRenderer();
        const double zoom = 0.1;
        DrawZoomedOut(renderer, zoom, null);
        renderer.IssuedGeometryCount.ShouldBeGreaterThan(1, "the first sighting draws vectors — no bitmap for one-off geometry");

        renderer.ResetDrawCounters();
        using var bitmap = DrawZoomedOut(renderer, zoom, null);

        renderer.IssuedGeometryCount.ShouldBe(1, "one cached image for the whole outline list");
        CountLitPixels(bitmap, new PixelRect(0, 0, 400, 5)).ShouldBeGreaterThan(100,
            "the cached image still paints the outlines");
    }

    [AvaloniaFact]
    public void ZoomedOut_LayerVisibilityChange_DoesNotReuseTheOldBitmap()
    {
        var renderer = new ComponentOutlineRenderer();
        var visibility = new GdsLayerVisibilityState();
        DrawZoomedOut(renderer, 0.1, visibility).Dispose();
        DrawZoomedOut(renderer, 0.1, visibility).Dispose();

        visibility.Set(1, 0, isVisible: false, opacity: 1.0);
        renderer.ResetDrawCounters();
        using var hidden = DrawZoomedOut(renderer, 0.1, visibility);

        CountLitPixels(hidden, new PixelRect(0, 0, 400, 5)).ShouldBe(0, "a hidden layer must not paint from a stale bitmap");
    }

    private static RenderTargetBitmap DrawZoomedOut(ComponentOutlineRenderer renderer, double zoom, GdsLayerVisibilityState? visibility)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(500, 100));
        using var ctx = bitmap.CreateDrawingContext();
        ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 500, 100));
        using (ctx.PushTransform(Matrix.CreateScale(zoom, zoom)))
            renderer.Draw(ctx, 0, 0, 4000, 50, 0, ManyPolygons, false, zoom, layerVisibility: visibility);
        return bitmap;
    }

    [AvaloniaFact]
    public void ZoomedIn_StaysVector()
    {
        var renderer = new ComponentOutlineRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 100));
        using (var ctx = bitmap.CreateDrawingContext())
            renderer.Draw(ctx, 0, 0, 4000, 50, 0, ManyPolygons, false, zoom: 1.0);

        renderer.IssuedGeometryCount.ShouldBe(8, "400 same-size polygons spanning 4 mm: one batched vector draw per 500 µm tile");
    }

    [AvaloniaFact]
    public void ZoomedIn_OffScreenTilesAreSkipped()
    {
        var renderer = new ComponentOutlineRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 100));
        using (var ctx = bitmap.CreateDrawingContext())
            renderer.Draw(ctx, 0, 0, 4000, 50, 0, ManyPolygons, false, zoom: 1.0,
                visibleWorld: new Rect(0, 0, 200, 100));

        renderer.IssuedGeometryCount.ShouldBe(1, "only the tile under the 200 µm viewport is drawn");
    }

    private static int CountLitPixels(RenderTargetBitmap bitmap, PixelRect region)
    {
        int size = region.Width * region.Height * 4;
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
        try
        {
            bitmap.CopyPixels(region, buffer, size, region.Width * 4);
            int lit = 0;
            for (int i = 0; i < region.Width * region.Height; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (System.Runtime.InteropServices.Marshal.ReadByte(buffer, i * 4 + c) > 10)
                    {
                        lit++;
                        break;
                    }
                }
            }
            return lit;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
    }
}
