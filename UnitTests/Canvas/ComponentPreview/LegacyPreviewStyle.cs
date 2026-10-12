using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Export;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>
/// The canvas-preview styling of release 0.16.0 (before #1493): rasterisation at one pixel
/// per micrometer, the waveguide color only on layer 1, flat grey on every other layer, and
/// full-cell outline polygons painted over the core. The visibility journey test rasterises
/// the real component geometry through this style to prove its pixel assertions would have
/// caught the invisible-preview regression: under this style the share of waveguide-colored
/// pixels collapses below the test's threshold.
/// </summary>
internal static class LegacyPreviewStyle
{
    /// <summary>Lower bound on bitmap dimensions, as in the 0.16.0 renderer.</summary>
    private const int MinBitmapPixels = 16;

    private const byte LegacyWaveguideAlpha = 0xD0;
    private const byte LegacyOtherAlpha = 0x70;

    /// <summary>Only layer 1 carried the waveguide color before #1493.</summary>
    private const int LegacyWaveguideLayer = 1;

    private static readonly IBrush WaveguideBrush =
        CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Waveguide, LegacyWaveguideAlpha));
    private static readonly IBrush OtherBrush =
        CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray300, LegacyOtherAlpha));

    /// <summary>
    /// Rasterises <paramref name="result"/> exactly like the 0.16.0 renderer did: the bitmap
    /// gets one pixel per micrometer of footprint, polygons paint in file order (outlines
    /// included, so a full-cell slab buries the core) and only layer 1 is waveguide-colored.
    /// Must be called on the UI thread. Returns null for a degenerate bounding box.
    /// </summary>
    public static RenderTargetBitmap? Rasterize(
        NazcaPreviewResult result, double widthMicrometers, double heightMicrometers)
    {
        double bboxW = result.XMax - result.XMin;
        double bboxH = result.YMax - result.YMin;
        if (bboxW <= 0 || bboxH <= 0)
            return null;

        int width = Math.Max(MinBitmapPixels, (int)Math.Ceiling(widthMicrometers));
        int height = Math.Max(MinBitmapPixels, (int)Math.Ceiling(heightMicrometers));
        double scaleX = width / bboxW;
        double scaleY = height / bboxH;

        var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        using var context = bitmap.CreateDrawingContext();
        foreach (var poly in result.Polygons)
        {
            var geometry = BuildPolygonGeometry(poly.Vertices, result.XMin, result.YMax, scaleX, scaleY);
            context.DrawGeometry(
                poly.Layer == LegacyWaveguideLayer ? WaveguideBrush : OtherBrush, null, geometry);
        }
        return bitmap;
    }

    private static StreamGeometry BuildPolygonGeometry(
        IReadOnlyList<(double X, double Y)> vertices, double xMin, double yMax, double scaleX, double scaleY)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        bool first = true;
        foreach (var (nazcaX, nazcaY) in vertices)
        {
            var point = new Point((nazcaX - xMin) * scaleX, (yMax - nazcaY) * scaleY);
            if (first) { ctx.BeginFigure(point, true); first = false; }
            else ctx.LineTo(point);
        }
        if (!first) ctx.EndFigure(true);
        return geometry;
    }
}
