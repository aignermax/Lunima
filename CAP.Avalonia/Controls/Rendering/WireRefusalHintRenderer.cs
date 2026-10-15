using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>A refused wire request: the hint text and the canvas point (µm) it is shown at.</summary>
/// <param name="Text">Localized explanation ("Chips connect only through edge couplers").</param>
/// <param name="X">Canvas X in micrometers.</param>
/// <param name="Y">Canvas Y in micrometers.</param>
/// <param name="ShownAtUtc">When the request was refused; the hint fades after <see cref="WireRefusalHintRenderer.VisibleFor"/>.</param>
public sealed record WireRefusalHint(string Text, double X, double Y, DateTime ShownAtUtc);

/// <summary>
/// Draws the hint of a refused cross-die wire right where the user let go: a danger-tinted
/// callout with the localized reason, so the refusal is seen where the attempt happened,
/// not only in the status bar. Screen-sized text at any zoom; hidden after a few seconds.
/// </summary>
public static class WireRefusalHintRenderer
{
    /// <summary>How long a refusal hint stays on the canvas.</summary>
    public static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(4);

    private const double FontPx = 13.0;
    private const double PaddingPx = 7.0;
    private const double OffsetPx = 16.0;
    private const double MarkerRadiusPx = 5.0;
    private const double BorderPx = 1.5;
    private const double MaxWidthPx = 300.0;

    /// <summary>Draws <paramref name="hint"/> (no-op for null or an expired hint).</summary>
    public static void Render(DrawingContext context, WireRefusalHint? hint, double zoom, DateTime nowUtc)
    {
        if (hint == null || nowUtc - hint.ShownAtUtc > VisibleFor)
            return;
        var text = new FormattedText(hint.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            CanvasTypography.UiSemiBold, FontPx / zoom, CanvasPalette.TextPrimaryBrush)
        {
            MaxTextWidth = MaxWidthPx / zoom,
        };
        double pad = PaddingPx / zoom, offset = OffsetPx / zoom, radius = MarkerRadiusPx / zoom;
        var anchor = new Point(hint.X, hint.Y);
        var box = new Rect(hint.X + offset, hint.Y - text.Height / 2 - pad, text.Width + 2 * pad, text.Height + 2 * pad);
        var border = new Pen(CanvasPalette.DangerBrush, BorderPx / zoom);
        context.DrawEllipse(CanvasPalette.DangerBrush, null, anchor, radius, radius);
        context.DrawLine(border, anchor, new Point(box.X, hint.Y));
        context.DrawRectangle(CanvasPalette.LabelBackplateBrush, border, box, pad, pad);
        context.DrawText(text, new Point(box.X + pad, box.Y + pad));
    }
}
