using Avalonia;
using Avalonia.Media;
using CAP_Core.Analysis;

namespace CAP.Avalonia.Controls.Rendering.WorkspaceEmphasis;

/// <summary>
/// Marks the findings of the last design-check run on the canvas: a warning diamond with a
/// "!" for every design problem, a faint info ring for every hint (e.g. an unconnected pin).
/// Screen-constant sizes so the markers read at any zoom. Drawn only while
/// <see cref="ViewModels.Canvas.ViewSettings.CanvasViewSettings.ShowDesignCheckMarkers"/> is on
/// (Layout's default) and the findings still describe the design.
/// </summary>
public static class DesignCheckMarkerRenderer
{
    private const double ProblemHalfDiagonalPx = 8.0;
    /// <summary>Wider than a pin glyph, so a hint on an open pin reads as a halo around it.</summary>
    private const double HintRingRadiusPx = 11.0;
    private const double HintRingStrokePx = 1.5;
    private const double OutlinePx = 1.5;
    private const byte HintAlpha = 0xC0;

    private static readonly IBrush GlyphBrush = CanvasPalette.Brush(CanvasPalette.Gray950);
    private static readonly IBrush ShadowBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray950, 0xB0));

    /// <summary>Draws the markers in world space (call inside the pan/zoom transform).</summary>
    public static void Render(DrawingContext context, CanvasRenderContext rc)
    {
        var settings = rc.ViewModel.ViewSettings;
        if (!settings.ShowDesignCheckMarkers || !settings.HasDesignCheckFindings)
            return;
        double zoom = rc.Zoom <= 0 ? 1.0 : rc.Zoom;
        var hintPen = rc.Pens.ScreenPen(CanvasPalette.WithAlpha(CanvasPalette.Info, HintAlpha), HintRingStrokePx);
        foreach (var finding in settings.DesignCheckFindings)
        {
            var center = new Point(finding.X, finding.Y);
            if (finding.Severity == DesignIssueSeverity.Hint)
                context.DrawEllipse(null, hintPen, center, HintRingRadiusPx / zoom, HintRingRadiusPx / zoom);
            else
                DrawProblem(context, center, zoom);
        }
    }

    /// <summary>Warning diamond with a dark "!" — bar and dot from rectangles, no text shaping.</summary>
    private static void DrawProblem(DrawingContext context, Point center, double zoom)
    {
        double r = ProblemHalfDiagonalPx / zoom;
        context.DrawGeometry(ShadowBrush, null, Diamond(center, r + OutlinePx / zoom));
        context.DrawGeometry(CanvasPalette.WarningBrush, null, Diamond(center, r));

        double barWidth = r * 0.22;
        var bar = new Rect(center.X - barWidth / 2, center.Y - r * 0.5, barWidth, r * 0.6);
        context.DrawRectangle(GlyphBrush, null, bar, barWidth / 2, barWidth / 2);
        context.DrawEllipse(GlyphBrush, null, new Point(center.X, center.Y + r * 0.32), barWidth * 0.6, barWidth * 0.6);
    }

    private static StreamGeometry Diamond(Point center, double r)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(center.X, center.Y - r), isFilled: true);
        ctx.LineTo(new Point(center.X + r, center.Y));
        ctx.LineTo(new Point(center.X, center.Y + r));
        ctx.LineTo(new Point(center.X - r, center.Y));
        ctx.EndFigure(isClosed: true);
        return geometry;
    }
}
