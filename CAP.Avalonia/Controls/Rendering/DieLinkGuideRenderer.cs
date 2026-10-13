using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CAP_Core.Analysis;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Live feedback while a die snaps onto a neighbour: a dashed guide along each aligned
/// facet axis and a "gap · loss" label, the loss computed with the same coupling model the
/// simulation applies to the link (so the number on the canvas is the number in the run).
/// </summary>
public static class DieLinkGuideRenderer
{
    /// <summary>Wavelength the live loss label is evaluated at (standard C-band).</summary>
    public const double LabelWavelengthNm = 1550.0;

    private const double GuideOvershootPx = 40.0;
    private const double GuidePx = 1.5;
    private const double DotRadiusPx = 4.0;
    private const double LabelFontPx = 12.0;
    private const double LabelOffsetPx = 14.0;
    private const double LabelPaddingPx = 4.0;
    private const double DecibelsPerPowerDecade = 10.0;
    private static readonly double[] Dash = { 5.0, 4.0 };

    /// <summary>Draws the guides and labels of <paramref name="snap"/> (no-op for null).</summary>
    public static void Render(DrawingContext context, DieLinkSnap? snap, double zoom)
    {
        if (snap is not { Pairs.Count: > 0 })
            return;
        var pen = new ImmutablePen(CanvasPalette.Brush(CanvasPalette.AccentHover), GuidePx / zoom, new ImmutableDashStyle(Dash, 0));
        foreach (var pair in snap.Pairs)
            DrawPair(context, pair, snap.GapMicrometers, pen, zoom);
    }

    /// <summary>Power loss in dB of a facet pair at <paramref name="gapMicrometers"/>, on axis.</summary>
    public static double LossDecibels(DieLinkPair pair, double gapMicrometers)
    {
        double eta = ChipletEdgeCouplerCoupling.PowerCoupling(
            ChipletEdgeCouplerCoupling.WaistOf(pair.MovingFacet.ParentComponent),
            ChipletEdgeCouplerCoupling.WaistOf(pair.FixedFacet.ParentComponent),
            0, gapMicrometers, LabelWavelengthNm);
        return Math.Max(0.0, -DecibelsPerPowerDecade * Math.Log10(eta));
    }

    private static void DrawPair(DrawingContext context, DieLinkPair pair, double gap, IPen pen, double zoom)
    {
        var (ax, ay) = pair.MovingFacet.GetAbsolutePosition();
        var (bx, by) = pair.FixedFacet.GetAbsolutePosition();
        double radians = pair.MovingFacet.GetAbsoluteAngle() * Math.PI / 180.0;
        double ox = Math.Cos(radians) * GuideOvershootPx / zoom, oy = Math.Sin(radians) * GuideOvershootPx / zoom;
        context.DrawLine(pen, new Point(ax - ox, ay - oy), new Point(bx + ox, by + oy));
        double r = DotRadiusPx / zoom;
        context.DrawEllipse(CanvasPalette.AccentHoverBrush, null, new Point(ax, ay), r, r);
        context.DrawEllipse(CanvasPalette.AccentHoverBrush, null, new Point(bx, by), r, r);

        var text = new FormattedText(
            string.Format(CultureInfo.CurrentCulture, "{0:0.0} µm · {1:0.00} dB", gap, LossDecibels(pair, gap)),
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, CanvasTypography.UiSemiBold,
            LabelFontPx / zoom, CanvasPalette.TextPrimaryBrush);
        double pad = LabelPaddingPx / zoom;
        var origin = new Point((ax + bx) / 2 - text.Width / 2, Math.Min(ay, by) - LabelOffsetPx / zoom - text.Height);
        context.DrawRectangle(CanvasPalette.LabelBackplateBrush, null,
            new Rect(origin.X - pad, origin.Y - pad, text.Width + 2 * pad, text.Height + 2 * pad), pad, pad);
        context.DrawText(text, origin);
    }
}
