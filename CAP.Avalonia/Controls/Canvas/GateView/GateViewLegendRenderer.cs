using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.GateView;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>
/// Screen-space chip at the canvas's top-left while the gate view is shown: names the view,
/// explains the net colours (1 / 0 / not run yet), and says when the view is still being
/// built or the design has no gates — so an empty canvas never looks like a bug.
/// </summary>
internal static class GateViewLegendRenderer
{
    private const double Margin = 12;
    private const double ChipHeight = 26;
    private const double PadX = 10;
    private const double SwatchLength = 16;
    private const double ItemGap = 12;
    private const double SwatchGap = 5;
    private const double FontSize = 12;
    private const double ChipRadius = 6;
    private const double SwatchStroke = 2;

    private static readonly IBrush ChipBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray800, 0xE0));
    private static readonly IPen ChipBorderPen = new Pen(CanvasPalette.Brush(CanvasPalette.Gray700), 1);

    /// <summary>Draws the legend chip (screen coordinates, outside the pan/zoom transform).</summary>
    public static void Render(DrawingContext context, Rect bounds, GateViewViewModel gateView)
    {
        var loc = LocalizationService.Instance;
        var title = Text(loc.Translate("GateView.LegendTitle"), CanvasPalette.TextPrimaryBrush);
        var status = StatusText(gateView, loc);
        var items = new (string Label, GateNetLevel Level)[]
        {
            ("1", GateNetLevel.One), ("0", GateNetLevel.Zero), (loc.Translate("GateView.LegendUnknown"), GateNetLevel.Unknown),
        };
        var labels = items.Select(i => Text(i.Label, GateViewPens.BrushFor(i.Level))).ToList();
        var width = PadX * 2 + title.Width + labels.Sum(l => ItemGap + SwatchLength + SwatchGap + l.Width)
                    + (status == null ? 0 : ItemGap + status.Width);
        var chip = new Rect(Margin, Margin, width, ChipHeight);
        context.DrawRectangle(ChipBrush, ChipBorderPen, chip, ChipRadius, ChipRadius);

        var x = chip.X + PadX;
        var midY = chip.Y + ChipHeight / 2;
        x = DrawText(context, title, x, midY);
        var pens = GateViewPens.For(1);
        for (var i = 0; i < items.Length; i++)
        {
            x += ItemGap;
            var pen = pens.ForLevel(items[i].Level);
            context.DrawLine(new Pen(pen.Brush, SwatchStroke, pen.DashStyle), new Point(x, midY), new Point(x + SwatchLength, midY));
            x = DrawText(context, labels[i], x + SwatchLength + SwatchGap, midY);
        }
        if (status != null)
            DrawText(context, status, x + ItemGap, midY);
    }

    private static FormattedText? StatusText(GateViewViewModel gateView, LocalizationService loc)
    {
        if (gateView.Graph.Nodes.Count > 0)
            return null;
        return gateView.IsRebuilding || gateView.IsStale
            ? Text(loc.Translate("GateView.Building"), CanvasPalette.TextTertiaryBrush)
            : Text(loc.Translate("GateView.NoGates"), CanvasPalette.WarningBrush);
    }

    private static double DrawText(DrawingContext context, FormattedText text, double x, double midY)
    {
        context.DrawText(text, new Point(x, midY - text.Height / 2));
        return x + text.Width;
    }

    private static FormattedText Text(string value, IBrush brush) => new(
        value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, CanvasTypography.UiMedium, FontSize, brush);
}
