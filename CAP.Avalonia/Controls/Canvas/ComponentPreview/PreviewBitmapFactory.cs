using Avalonia.Media.Imaging;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Rasterises a <see cref="NazcaPreviewResult"/>'s polygons into a <see cref="Bitmap"/>
/// for display (e.g. the per-instance Nazca code editor's live preview). Preserves the
/// geometry's aspect ratio within a fixed pixel budget.
/// </summary>
public static class PreviewBitmapFactory
{
    /// <summary>Default pixel budget for the longer bounding-box side.</summary>
    public const int DefaultPixels = 256;

    /// <summary>
    /// Returns a bitmap of the result's polygons, or null when there is nothing to draw
    /// (no polygons / degenerate bbox) or when no rendering backend is available
    /// (e.g. headless unit tests) — all non-fatal.
    /// </summary>
    public static Bitmap? FromResult(NazcaPreviewResult result, int pixels = DefaultPixels)
    {
        if (result.Polygons.Count == 0)
            return null;

        double bboxW = result.XMax - result.XMin;
        double bboxH = result.YMax - result.YMin;
        if (bboxW <= 0 || bboxH <= 0)
            return null;

        // Fit the longer side to the pixel budget so wide/tall cells keep their shape.
        double scale = pixels / System.Math.Max(bboxW, bboxH);
        int w = System.Math.Max(1, (int)System.Math.Round(bboxW * scale));
        int h = System.Math.Max(1, (int)System.Math.Round(bboxH * scale));

        try
        {
            return GdsPolygonRenderer.RasterizeToBitmap(result, w, h);
        }
        catch
        {
            // RenderTargetBitmap needs a rendering backend; absent in headless tests.
            return null;
        }
    }
}
