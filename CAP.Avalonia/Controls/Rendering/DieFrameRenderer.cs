using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Draws a die of a multi-die assembly: its outline in the process color, the edge zone as
/// a faint inner band (where edge couplers snap), the die name with its process above the
/// frame and the outline size below it. Pens and fonts are sized in screen pixels so the
/// frame reads the same at every zoom.
/// </summary>
public static class DieFrameRenderer
{
    private const double FramePx = 1.5;
    private const double SelectedFramePx = 3.0;
    private const double EdgeZoneLinePx = 1.0;
    private const double NameFontPx = 13.0;
    private const double CaptionFontPx = 11.0;
    private const double LabelGapPx = 6.0;
    private const double CornerTickPx = 14.0;
    private const byte EdgeZoneFillAlpha = 0x14;
    private const byte EdgeZoneLineAlpha = 0x55;
    private const double MicrometersPerMillimeter = 1000.0;
    private static readonly double[] EdgeZoneDash = { 4.0, 4.0 };

    /// <summary>Fills each die's area in screen space (beneath the grid lines), like the single chip.</summary>
    public static void RenderFills(DrawingContext context, IEnumerable<ComponentGroup> dies, double zoom, double panX, double panY)
    {
        foreach (var die in dies)
        {
            var o = DieAssembly.OutlineOf(die);
            context.FillRectangle(CanvasPalette.ChipFillBrush,
                new Rect(o.X * zoom + panX, o.Y * zoom + panY, o.Width * zoom, o.Height * zoom));
        }
    }

    /// <summary>Draws one die's frame, edge zone and labels in world space.</summary>
    /// <param name="context">Drawing context inside the zoom/pan transform.</param>
    /// <param name="die">The die group.</param>
    /// <param name="isSelected">True when the die is selected (thicker accent frame).</param>
    /// <param name="isHovered">True when the pointer hovers the die.</param>
    /// <param name="zoom">Current zoom (screen px per µm).</param>
    public static void Render(DrawingContext context, ComponentGroup die, bool isSelected, bool isHovered, double zoom)
    {
        var outline = DieAssembly.OutlineOf(die);
        var rect = new Rect(outline.X, outline.Y, outline.Width, outline.Height);
        var color = DieProcessColors.For(die.ProcessBinding);

        DrawEdgeZone(context, rect, die.Die!.EdgeZoneMicrometers, color, zoom);

        var framePen = new ImmutablePen(CanvasPalette.Brush(color), FramePx / zoom);
        context.DrawRectangle(null, framePen, rect);
        DrawCornerTicks(context, rect, new ImmutablePen(CanvasPalette.Brush(color), SelectedFramePx / zoom), CornerTickPx / zoom);
        if (isSelected || isHovered)
        {
            var accent = isSelected ? CanvasPalette.AccentHover : CanvasPalette.WithAlpha(CanvasPalette.AccentHover, 0x80);
            var inflate = SelectedFramePx / zoom;
            context.DrawRectangle(null, new ImmutablePen(CanvasPalette.Brush(accent), SelectedFramePx / zoom), rect.Inflate(inflate));
        }

        DrawNameLabel(context, die, rect, color, zoom);
        DrawSizeLabel(context, outline, rect, zoom);
    }

    /// <summary>"2.00 × 2.00 mm" for mm-scale outlines, "800 × 600 µm" below one millimeter.</summary>
    public static string FormatSize(double widthMicrometers, double heightMicrometers, CultureInfo culture)
    {
        if (widthMicrometers >= MicrometersPerMillimeter && heightMicrometers >= MicrometersPerMillimeter)
            return string.Format(culture, "{0:0.00} × {1:0.00} mm",
                widthMicrometers / MicrometersPerMillimeter, heightMicrometers / MicrometersPerMillimeter);
        return string.Format(culture, "{0:0.#} × {1:0.#} µm", widthMicrometers, heightMicrometers);
    }

    private static void DrawEdgeZone(DrawingContext context, Rect rect, double edgeZone, Color color, double zoom)
    {
        if (edgeZone <= 0 || edgeZone * 2 >= Math.Min(rect.Width, rect.Height))
            return;
        var inner = rect.Deflate(edgeZone);
        var band = new RectangleGeometry(rect);
        var hole = new RectangleGeometry(inner);
        context.DrawGeometry(CanvasPalette.Brush(CanvasPalette.WithAlpha(color, EdgeZoneFillAlpha)), null,
            new CombinedGeometry(GeometryCombineMode.Exclude, band, hole));
        var dashPen = new ImmutablePen(CanvasPalette.Brush(CanvasPalette.WithAlpha(color, EdgeZoneLineAlpha)),
            EdgeZoneLinePx / zoom, new ImmutableDashStyle(EdgeZoneDash, 0));
        context.DrawRectangle(null, dashPen, inner);
    }

    private static void DrawNameLabel(DrawingContext context, ComponentGroup die, Rect rect, Color color, double zoom)
    {
        var name = new FormattedText(die.GroupName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            CanvasTypography.UiSemiBold, NameFontPx / zoom, CanvasPalette.TextPrimaryBrush);
        double y = rect.Top - (LabelGapPx / zoom) - name.Height;
        context.DrawText(name, new Point(rect.Left, y));

        var processName = die.ProcessBinding?.DisplayName;
        if (string.IsNullOrWhiteSpace(processName))
            return;
        var caption = new FormattedText("  " + processName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            CanvasTypography.Ui, CaptionFontPx / zoom, CanvasPalette.Brush(color));
        context.DrawText(caption, new Point(rect.Left + name.Width, y + (name.Height - caption.Height)));
    }

    private static void DrawSizeLabel(DrawingContext context, DieOutline outline, Rect rect, double zoom)
    {
        var text = new FormattedText(FormatSize(outline.Width, outline.Height, CultureInfo.CurrentCulture),
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, CanvasTypography.Ui,
            CaptionFontPx / zoom, CanvasPalette.TextTertiaryBrush);
        context.DrawText(text, new Point(rect.Left, rect.Bottom + LabelGapPx / zoom));
    }

    private static void DrawCornerTicks(DrawingContext context, Rect r, IPen pen, double len)
    {
        context.DrawLine(pen, new Point(r.Left, r.Top + len), new Point(r.Left, r.Top));
        context.DrawLine(pen, new Point(r.Left, r.Top), new Point(r.Left + len, r.Top));
        context.DrawLine(pen, new Point(r.Right - len, r.Top), new Point(r.Right, r.Top));
        context.DrawLine(pen, new Point(r.Right, r.Top), new Point(r.Right, r.Top + len));
        context.DrawLine(pen, new Point(r.Left, r.Bottom - len), new Point(r.Left, r.Bottom));
        context.DrawLine(pen, new Point(r.Left, r.Bottom), new Point(r.Left + len, r.Bottom));
        context.DrawLine(pen, new Point(r.Right - len, r.Bottom), new Point(r.Right, r.Bottom));
        context.DrawLine(pen, new Point(r.Right, r.Bottom), new Point(r.Right, r.Bottom - len));
    }
}
