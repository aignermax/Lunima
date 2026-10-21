using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit.Abstractions;

namespace UnitTests.Rendering;

public class OutlineZoomOutCacheTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void ZoomingOut_ReusesSharperBitmapImmediately_WithoutVectorFallback()
    {
        var polygons = Polygons(10000);
        var renderer = new ComponentOutlineRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 800));
        Draw(renderer, bitmap, polygons, 0.125);
        Draw(renderer, bitmap, polygons, 0.125);
        var samples = new List<double>();
        var draws = new List<long>();
        foreach (double zoom in new[] { 0.06, 0.03, 0.015, 0.007 })
        {
            renderer.ResetDrawCounters();
            var timer = Stopwatch.StartNew();
            Draw(renderer, bitmap, polygons, zoom);
            samples.Add(timer.Elapsed.TotalMilliseconds);
            draws.Add(renderer.IssuedGeometryCount);
        }
        output.WriteLine($"Zoom-out frames ms: {string.Join(", ", samples.Select(v => v.ToString("F2")))}; draws: {string.Join(", ", draws)}");
        draws.ShouldAllBe(count => count == 1,
            "a sharper cached image already contains everything needed for each zoom-out frame");
    }

    [AvaloniaFact]
    public void ZoomingOut_WithChangedVisibility_DoesNotReuseVisibleBitmap()
    {
        var polygons = Polygons(100);
        var renderer = new ComponentOutlineRenderer();
        var visibility = new GdsLayerVisibilityState();
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 800));
        Draw(renderer, bitmap, polygons, 0.125, visibility);
        Draw(renderer, bitmap, polygons, 0.125, visibility);
        visibility.Set(1, 0, false, 1);
        renderer.ResetDrawCounters();
        Draw(renderer, bitmap, polygons, 0.06, visibility);
        renderer.IssuedGeometryCount.ShouldBe(0);
    }

    private static OutlinePolygon[] Polygons(int count) => Enumerable.Range(0, count).Select(i =>
    {
        double x = (i % 100) * 50, y = (i / 100) * 50;
        return new OutlinePolygon { Layer = 1, Points = new[] {
            new OutlinePoint(x,y), new OutlinePoint(x+40,y),
            new OutlinePoint(x+40,y+40), new OutlinePoint(x,y+40) } };
    }).ToArray();

    private static void Draw(ComponentOutlineRenderer renderer, RenderTargetBitmap bitmap,
        OutlinePolygon[] polygons, double zoom, GdsLayerVisibilityState? visibility = null)
    {
        using var context = bitmap.CreateDrawingContext();
        using (context.PushTransform(Matrix.CreateScale(zoom, zoom)))
            renderer.Draw(context, 0, 0, 5000, 5000, 0, polygons, false, zoom,
                layerVisibility: visibility);
    }
}
