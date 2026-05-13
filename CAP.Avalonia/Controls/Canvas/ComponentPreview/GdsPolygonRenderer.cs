using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Draws GDS polygons for a component onto the design canvas.
/// Handles coordinate transform from Nazca µm space to canvas-pixel space,
/// Y-axis flip, component rotation, and layer-based colouring.
/// </summary>
public static class GdsPolygonRenderer
{
    // ── Layer colour palette ────────────────────────────────────────────────
    // Add new layers here without touching any other file.

    private static readonly Color WaveguideColor = Color.FromArgb(180, 100, 160, 220); // layer 1/0
    private static readonly Color PortColor      = Color.FromArgb(140,  60, 200, 120); // layer 1/10 PinRec
    private static readonly Color DefaultColor   = Color.FromArgb(120, 160, 160, 160); // all other layers

    private const int WaveguideLayer = 1;
    private const int PortLayer      = 10;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renders GDS polygons for <paramref name="comp"/> using
    /// <paramref name="previewData"/>. No-ops when the result has no polygons.
    /// </summary>
    /// <param name="context">Avalonia drawing context (world-space transform already active).</param>
    /// <param name="previewData">Cached preview data for the component template.</param>
    /// <param name="comp">Target component providing position and rotation.</param>
    public static void DrawGdsPreview(
        DrawingContext context,
        GdsPreviewData previewData,
        ComponentViewModel comp)
    {
        var result = previewData.Result;
        if (result.Polygons.Count == 0)
            return;

        double bboxW = result.XMax - result.XMin;
        double bboxH = result.YMax - result.YMin;
        if (bboxW <= 0 || bboxH <= 0)
            return;

        double scaleX = comp.Width  / bboxW;
        double scaleY = comp.Height / bboxH;

        double centerX = comp.X + comp.Width  / 2.0;
        double centerY = comp.Y + comp.Height / 2.0;

        using (context.PushTransform(BuildRotationMatrix(comp.Component.RotationDegrees, centerX, centerY)))
        {
            foreach (var poly in result.Polygons)
            {
                var geometry = BuildPolygonGeometry(
                    poly.Vertices, result.XMin, result.YMax, scaleX, scaleY, comp.X, comp.Y);
                var brush = new SolidColorBrush(LayerColor(poly.Layer));
                context.DrawGeometry(brush, null, geometry);
            }
        }
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

    private static Color LayerColor(int layer) => layer switch
    {
        WaveguideLayer => WaveguideColor,
        PortLayer      => PortColor,
        _              => DefaultColor,
    };
}
