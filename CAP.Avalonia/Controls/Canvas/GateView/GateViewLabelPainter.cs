using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Logic.GateView;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP_Core.Analysis.LogicAnalysis.GateView;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>
/// Text of the gate view: gate names under symbols, net names at their driver, port flag
/// names, register pin letters. Labels are screen-constant (always readable) and appear
/// by level of detail: signal-named nets and ports early, raw <c>gate.pin</c> ids only
/// once zoomed in far enough that they cannot crowd each other — a CPU-sized overview
/// would otherwise drown in hundreds of names.
/// </summary>
internal static class GateViewLabelPainter
{
    private const double LabelPx = 11.0;
    private const double GapPx = 3.0;
    private const double TypicalBodyHeight = 150.0;
    private const double NamedLabelMinBodyPx = 36.0;
    private const double RawLabelMinBodyPx = 110.0;
    private const double GateNameMinBodyPx = 36.0;
    private const double SymbolTextMinBodyPx = 26.0;
    private const double BlockNameMinPx = 24.0;
    private const double PinLetterShare = 0.2;
    private const double UnknownMarkShare = 0.42;

    /// <summary>Gate name centred under the symbol body.</summary>
    public static void GateName(CanvasRenderContext rc, GateSymbolPlacement symbol)
    {
        if (TypicalBodyHeight * rc.Zoom < GateNameMinBodyPx)
            return;
        var text = Text(symbol.Node.DisplayName, rc.Zoom, CanvasPalette.TextTertiaryBrush);
        var origin = new Point(symbol.Body.Center.X - text.Width / 2, symbol.Body.Bottom + GapPx / rc.Zoom);
        rc.Labels.Enqueue(text, CanvasPalette.TextTertiaryBrush, origin);
    }

    /// <summary>Net name just above the wire leaving the driver.</summary>
    public static void NetLabel(CanvasRenderContext rc, GateNetRoute route, GateNetLevel level)
    {
        if (route.LabelAnchor is not { } anchor || !IsShown(route.Net.IsNamed, rc.Zoom))
            return;
        var brush = GateViewPens.BrushFor(level);
        var text = Text(route.Net.Name, rc.Zoom, brush);
        rc.Labels.Enqueue(text, brush, new Point(anchor.X + GapPx / rc.Zoom, anchor.Y - text.Height - GapPx / rc.Zoom));
    }

    /// <summary>Port name beyond the end of an input or output flag.</summary>
    public static void FlagLabel(CanvasRenderContext rc, GateNetRoute route, GatePortFlag flag, Point flagEnd, GateNetLevel level)
    {
        // Unloaded outputs are the circuit's results: labelled as early as named nets, raw id or not.
        if (!IsShown(route.Net.IsNamed || flag.IsOutput, rc.Zoom))
            return;
        var brush = GateViewPens.BrushFor(level);
        var text = Text(route.Net.Name, rc.Zoom, brush);
        var gap = GapPx / rc.Zoom;
        var x = flag.IsOutput ? flagEnd.X + gap : flagEnd.X - gap - text.Width;
        rc.Labels.Enqueue(text, brush, new Point(x, flagEnd.Y - text.Height / 2));
    }

    /// <summary>Name of a non-gate block at its top-left corner.</summary>
    public static void BlockName(CanvasRenderContext rc, GateViewBlock block)
    {
        if (block.Bounds.Height * rc.Zoom < BlockNameMinPx)
            return;
        var text = Text(block.Name, rc.Zoom, CanvasPalette.TextTertiaryBrush);
        rc.Labels.Enqueue(text, CanvasPalette.TextTertiaryBrush,
            new Point(block.Bounds.Left, block.Bounds.Top - text.Height - GapPx / rc.Zoom));
    }

    /// <summary>World-scaled text inside a symbol: D/Q of a register, "?" of an unknown block.</summary>
    public static void SymbolText(DrawingContext context, GateSymbolPlacement symbol, double zoom)
    {
        var body = symbol.Body;
        if (!ShowsSymbolText(body.Height, zoom))
            return;
        var kind = symbol.Node.Kind;
        if (kind == GateSymbolKind.Register)
        {
            var size = body.Height * PinLetterShare;
            DrawWorld(context, "D", size, new Point(body.Left + size * 0.4, body.Top + size * 0.3));
            DrawWorld(context, "Q", size, new Point(body.Right - size * 1.1, body.Top + size * 0.3));
        }
        else if (kind == GateSymbolKind.Unknown)
        {
            var size = body.Height * UnknownMarkShare;
            var text = WorldText("?", size);
            context.DrawText(text, new Point(body.Center.X - text.Width / 2, body.Center.Y - text.Height / 2));
        }
    }

    /// <summary>True when a label of this kind is readable at the zoom without crowding.</summary>
    internal static bool IsShown(bool isNamed, double zoom) =>
        TypicalBodyHeight * zoom >= (isNamed ? NamedLabelMinBodyPx : RawLabelMinBodyPx);

    /// <summary>Minimum on-screen body height for symbol text.</summary>
    internal static bool ShowsSymbolText(double bodyHeight, double zoom) => bodyHeight * zoom >= SymbolTextMinBodyPx;

    private static void DrawWorld(DrawingContext context, string value, double size, Point origin) =>
        context.DrawText(WorldText(value, size), origin);

    private static FormattedText WorldText(string value, double size) => new(
        value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CanvasTypography.UiMedium, size,
        CanvasPalette.TextSecondaryBrush);

    private static FormattedText Text(string value, double zoom, IBrush brush) => new(
        value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CanvasTypography.Ui,
        LabelPx / (zoom <= 0 ? 1 : zoom), brush);
}
