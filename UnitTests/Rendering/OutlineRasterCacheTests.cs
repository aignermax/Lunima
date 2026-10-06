using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Rendering;
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
    public void ZoomedOut_DrawsOneImageInsteadOfThePolygons_AndPaintsThem()
    {
        var renderer = new ComponentOutlineRenderer();
        const double zoom = 0.1;
        using var bitmap = new RenderTargetBitmap(new PixelSize(500, 100));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 500, 100));
            using (ctx.PushTransform(Matrix.CreateScale(zoom, zoom)))
                renderer.Draw(ctx, 0, 0, 4000, 50, 0, ManyPolygons, false, zoom);
        }

        renderer.IssuedGeometryCount.ShouldBe(1, "one cached image for the whole outline list");
        CountLitPixels(bitmap, new PixelRect(0, 0, 400, 5)).ShouldBeGreaterThan(100,
            "the cached image still paints the outlines");
    }

    [AvaloniaFact]
    public void ZoomedIn_StaysVector()
    {
        var renderer = new ComponentOutlineRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 100));
        using (var ctx = bitmap.CreateDrawingContext())
            renderer.Draw(ctx, 0, 0, 4000, 50, 0, ManyPolygons, false, zoom: 1.0);

        renderer.IssuedGeometryCount.ShouldBe(1, "one batched vector draw for 400 same-size polygons");
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
