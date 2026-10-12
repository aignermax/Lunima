using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Draws GDS polygons for a component onto the design canvas.
/// Handles coordinate transform from Nazca µm space to canvas-pixel space,
/// Y-axis flip, component rotation, and layer-based colouring.
/// </summary>
public static class GdsPolygonRenderer
{
    /// <summary>Lower bound on bitmap dimensions to avoid zero-size bitmaps.</summary>
    internal const int MinBitmapPixels = 16;

    /// <summary>Pixel budget for the longer side of a canvas preview bitmap.</summary>
    internal const int TargetBitmapLongSidePixels = 768;

    /// <summary>
    /// Lower bound on the raster density. Waveguide cores are ~0.5 µm wide, so one pixel per
    /// micrometer already blurs them; long devices still get at least this density.
    /// </summary>
    private const double MinPixelsPerMicrometer = 1.0;

    /// <summary>Upper bound on the raster density, so tiny cells don't get oversized bitmaps.</summary>
    private const double MaxPixelsPerMicrometer = 8.0;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renders GDS polygons for <paramref name="comp"/> using
    /// <paramref name="previewData"/>. No-ops when the result has no polygons.
    /// When a pre-rasterised <see cref="GdsPreviewData.Bitmap"/> is available the
    /// method blits it directly (O(1) per frame). Otherwise it falls back to
    /// rebuilding geometry — this only occurs in the brief window between cache
    /// population and bitmap creation on the UI thread.
    /// </summary>
    /// <param name="context">Avalonia drawing context (world-space transform already active).</param>
    /// <param name="previewData">Cached preview data for the component template.</param>
    /// <param name="comp">Target component providing position and rotation.</param>
    public static void DrawGdsPreview(
        DrawingContext context,
        GdsPreviewData previewData,
        ComponentViewModel comp) =>
        DrawGdsPreview(context, previewData, comp.Component, comp.X, comp.Y, comp.Width, comp.Height);

    /// <summary>
    /// Draws the GDS preview of a component that has no view model of its own, such as a
    /// child inside a group; its footprint comes straight from the component.
    /// </summary>
    public static void DrawGdsPreview(DrawingContext context, GdsPreviewData previewData, Component component) =>
        DrawGdsPreview(context, previewData, component, component.PhysicalX, component.PhysicalY,
            component.WidthMicrometers, component.HeightMicrometers);

    /// <summary>
    /// Draws the GDS preview of <paramref name="component"/> into its current footprint
    /// <paramref name="footprint"/> (world coordinates, rotation-swapped like the live bounds).
    /// </summary>
    public static void DrawGdsPreview(DrawingContext context, GdsPreviewData previewData, Component component, Rect footprint) =>
        DrawGdsPreview(context, previewData, component, footprint.X, footprint.Y, footprint.Width, footprint.Height);

    private static void DrawGdsPreview(DrawingContext context, GdsPreviewData previewData, Component component,
        double x, double y, double width, double height)
    {
        var result = previewData.Result;
        if (result.Polygons.Count == 0)
            return;

        double centerX = x + width / 2.0;
        double centerY = y + height / 2.0;
        double rotationDegrees = component.RotationDegrees;

        // The rotate command already swapped the live width/height at 90°/270°, but the bitmap
        // holds UNROTATED geometry: draw it into the unrotated-size rect and let the rotation
        // transform map it onto the footprint — else the 90° swap is applied twice. For
        // non-cardinal rotations the recorded pre-rotation dims supply the frame.
        var destRect = GetUnrotatedDestRect(x, y, width, height, rotationDegrees,
            component.UnrotatedWidthMicrometers, component.UnrotatedHeightMicrometers);

        using (context.PushTransform(BuildRotationMatrix(rotationDegrees, centerX, centerY)))
        {
            if (previewData.Bitmap != null)
            {
                context.DrawImage(previewData.Bitmap, destRect);
                return;
            }

            // Fallback: draw the polygons directly until the bitmap is rasterised.
            DrawPolygonsAsGeometry(context, result, destRect.X, destRect.Y, destRect.Width, destRect.Height,
                previewData.LayerRoles);
        }
    }

    /// <summary>
    /// Rasterises all polygons in <paramref name="result"/> to a
    /// <see cref="RenderTargetBitmap"/> at the requested pixel dimensions.
    /// Must be called on the UI thread.
    /// Returns <c>null</c> if the bbox is degenerate or bitmap creation fails.
    /// </summary>
    /// <param name="roles">Core/metal layer assignment of the component's process;
    /// null falls back to the demo-PDK roles.</param>
    internal static RenderTargetBitmap? RasterizeToBitmap(NazcaPreviewResult result, int width, int height,
        GdsPreviewLayerRoles? roles = null)
    {
        double bboxW = result.XMax - result.XMin;
        double bboxH = result.YMax - result.YMin;
        if (bboxW <= 0 || bboxH <= 0)
            return null;

        double scaleX = width  / bboxW;
        double scaleY = height / bboxH;
        var effectiveRoles = roles ?? GdsPreviewLayerRoles.Default;

        try
        {
            var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
            using var ctx = bitmap.CreateDrawingContext();
            foreach (var poly in GdsPreviewLayerStyle.InDrawOrder(result, effectiveRoles))
            {
                var geo = BuildPolygonGeometry(poly.Vertices, result.XMin, result.YMax, scaleX, scaleY, 0, 0);
                ctx.DrawGeometry(GdsPreviewLayerStyle.BrushFor(poly.Layer, effectiveRoles), null, geo);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pixel size of the canvas preview bitmap for a footprint of
    /// <paramref name="widthMicrometers"/> × <paramref name="heightMicrometers"/>: the longer
    /// side gets <see cref="TargetBitmapLongSidePixels"/>, with the density clamped between
    /// <see cref="MinPixelsPerMicrometer"/> and <see cref="MaxPixelsPerMicrometer"/>.
    /// </summary>
    internal static (int Width, int Height) GetBitmapSize(double widthMicrometers, double heightMicrometers)
    {
        double longSide = Math.Max(widthMicrometers, heightMicrometers);
        double density = longSide > 0
            ? Math.Clamp(TargetBitmapLongSidePixels / longSide, MinPixelsPerMicrometer, MaxPixelsPerMicrometer)
            : MinPixelsPerMicrometer;
        return (Math.Max(MinBitmapPixels, (int)Math.Ceiling(widthMicrometers * density)),
                Math.Max(MinBitmapPixels, (int)Math.Ceiling(heightMicrometers * density)));
    }

    // ── Internal helpers (internal for unit-testing transform math) ─────────

    /// <summary>
    /// Transforms a single Nazca-space vertex to canvas-pixel space.
    /// Exposed as <c>internal</c> to allow transform-math unit tests.
    /// </summary>
    internal static (double CanvasX, double CanvasY) TransformVertex(
        double nazcaX, double nazcaY,
        double xMin, double yMax,
        double scaleX, double scaleY,
        double compX, double compY)
    {
        return (
            compX + (nazcaX - xMin) * scaleX,
            compY + (yMax  - nazcaY) * scaleY   // Y-flip: Nazca Y-up → screen Y-down
        );
    }

    /// <summary>
    /// Footprint size at RotationDegrees = 0. When the component recorded its
    /// pre-rotation dims (<paramref name="recordedUnrotatedWidth"/>/<paramref name="recordedUnrotatedHeight"/>)
    /// they win — for non-cardinal rotations the rotated AABB cannot reveal the
    /// original aspect. Otherwise odd quarter-turns swap the live Width/Height
    /// back. Accepts any 90°-multiple (normalises negatives and ≥360°).
    /// </summary>
    internal static (double Width, double Height) GetUnrotatedSize(
        double rotationDegrees, double currentWidth, double currentHeight,
        double recordedUnrotatedWidth = 0, double recordedUnrotatedHeight = 0)
    {
        if (recordedUnrotatedWidth > 0 && recordedUnrotatedHeight > 0)
            return (recordedUnrotatedWidth, recordedUnrotatedHeight);
        int quarterTurns = (((int)Math.Round(rotationDegrees / 90.0)) % 4 + 4) % 4;
        return quarterTurns % 2 == 0
            ? (currentWidth, currentHeight)
            : (currentHeight, currentWidth);
    }

    /// <summary>
    /// The rect the unrotated preview must be drawn into so that, after rotating around the
    /// footprint centre, the image covers the current (rotation-swapped) footprint exactly.
    /// </summary>
    internal static Rect GetUnrotatedDestRect(
        double compX, double compY, double compWidth, double compHeight, double rotationDegrees,
        double recordedUnrotatedWidth = 0, double recordedUnrotatedHeight = 0)
    {
        var (unrotatedW, unrotatedH) = GetUnrotatedSize(
            rotationDegrees, compWidth, compHeight, recordedUnrotatedWidth, recordedUnrotatedHeight);
        double centerX = compX + compWidth  / 2.0;
        double centerY = compY + compHeight / 2.0;
        return new Rect(centerX - unrotatedW / 2.0, centerY - unrotatedH / 2.0, unrotatedW, unrotatedH);
    }

    /// <summary>
    /// Builds a rotation-around-centre matrix.
    /// Positive <paramref name="degrees"/> = clockwise on screen (Y-down canvas).
    /// </summary>
    internal static Matrix BuildRotationMatrix(double degrees, double cx, double cy)
    {
        if (degrees == 0)
            return Matrix.Identity;

        double radians = degrees * Math.PI / 180.0;
        return Matrix.CreateTranslation(-cx, -cy)
             * Matrix.CreateRotation(radians)
             * Matrix.CreateTranslation(cx, cy);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    internal static void DrawPolygonsAsGeometry(
        DrawingContext context,
        NazcaPreviewResult result,
        double compX, double compY,
        double compWidth, double compHeight,
        GdsPreviewLayerRoles? roles = null)
    {
        double bboxW = result.XMax - result.XMin;
        double bboxH = result.YMax - result.YMin;
        if (bboxW <= 0 || bboxH <= 0)
            return;

        double scaleX = compWidth  / bboxW;
        double scaleY = compHeight / bboxH;
        var effectiveRoles = roles ?? GdsPreviewLayerRoles.Default;

        foreach (var poly in GdsPreviewLayerStyle.InDrawOrder(result, effectiveRoles))
        {
            var geo = BuildPolygonGeometry(poly.Vertices, result.XMin, result.YMax, scaleX, scaleY, compX, compY);
            context.DrawGeometry(GdsPreviewLayerStyle.BrushFor(poly.Layer, effectiveRoles), null, geo);
        }
    }

    private static StreamGeometry BuildPolygonGeometry(
        IReadOnlyList<(double X, double Y)> vertices,
        double xMin, double yMax,
        double scaleX, double scaleY,
        double compX, double compY)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        bool first = true;
        foreach (var (nx, ny) in vertices)
        {
            var (px, py) = TransformVertex(nx, ny, xMin, yMax, scaleX, scaleY, compX, compY);
            if (first) { ctx.BeginFigure(new Point(px, py), true); first = false; }
            else ctx.LineTo(new Point(px, py));
        }
        if (!first) ctx.EndFigure(true);
        return geo;
    }
}
