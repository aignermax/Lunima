using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP_Core.Analysis.LogicAnalysis.GateView;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>
/// Draws the distinctive-shape schematic symbols (IEEE 91): the D of AND, the shield of
/// OR, the doubled back of XOR, the triangle of NOT/buffer, an inversion bubble where the
/// function negates, a clocked box for registers and a plain box for anything else.
/// Geometry is in world µm; pens come from the caller so the stroke stays screen-constant.
/// </summary>
internal static class GateSymbolPainter
{
    private const double BubbleShare = 0.075;
    private const double XorGapShare = 0.12;
    private const double OrBackDepthShare = 0.22;
    private const double ClockShare = 0.16;

    /// <summary>Depth of the curved OR back at a typical pin height, as a share of the full back depth.</summary>
    private const double CurvedBackInset = 0.42;

    /// <summary>Draws one symbol: pin stubs, body and inversion bubble.</summary>
    public static void Draw(DrawingContext context, GateSymbolPlacement symbol, IBrush fill, IPen outline)
    {
        var body = symbol.Body;
        var kind = symbol.Node.Kind;
        DrawStubs(context, symbol, outline);
        context.DrawGeometry(fill, outline, BodyGeometry(kind, body));
        if (kind == GateSymbolKind.Xor || kind == GateSymbolKind.Xnor)
            context.DrawGeometry(null, outline, OrBack(body, -body.Height * XorGapShare));
        if (kind == GateSymbolKind.Register)
            context.DrawGeometry(null, outline, ClockMark(body));
        if (IsInverting(kind))
        {
            var radius = body.Height * BubbleShare;
            foreach (var pin in symbol.OutputPins)
                context.DrawEllipse(fill, outline, new Point(body.Right + radius, pin.Y), radius, radius);
        }
    }

    /// <summary>True for the functions drawn with an output inversion bubble.</summary>
    public static bool IsInverting(GateSymbolKind kind) =>
        kind is GateSymbolKind.Not or GateSymbolKind.Nand or GateSymbolKind.Nor or GateSymbolKind.Xnor;

    private static void DrawStubs(DrawingContext context, GateSymbolPlacement symbol, IPen pen)
    {
        var body = symbol.Body;
        var inset = InputInset(symbol.Node.Kind, body);
        foreach (var pin in symbol.InputPins)
            context.DrawLine(pen, pin, new Point(body.Left + inset, pin.Y));
        var bubble = IsInverting(symbol.Node.Kind) ? 2 * body.Height * BubbleShare : 0;
        foreach (var pin in symbol.OutputPins)
            context.DrawLine(pen, new Point(body.Right + bubble, pin.Y), pin);
    }

    /// <summary>How far an input stub reaches into the body (OR-family backs are curved).</summary>
    private static double InputInset(GateSymbolKind kind, Rect body) => kind switch
    {
        GateSymbolKind.Or or GateSymbolKind.Nor or GateSymbolKind.Xor or GateSymbolKind.Xnor
            => body.Height * OrBackDepthShare * CurvedBackInset,
        _ => 0,
    };

    private static Geometry BodyGeometry(GateSymbolKind kind, Rect body) => kind switch
    {
        GateSymbolKind.And or GateSymbolKind.Nand => AndShape(body),
        GateSymbolKind.Or or GateSymbolKind.Nor => OrShape(body, 0),
        GateSymbolKind.Xor or GateSymbolKind.Xnor => OrShape(body, body.Height * XorGapShare),
        GateSymbolKind.Not or GateSymbolKind.Buffer or GateSymbolKind.FanOut => Triangle(body),
        _ => new RectangleGeometry(body, body.Height * 0.06, body.Height * 0.06),
    };

    private static Geometry AndShape(Rect body)
    {
        var radius = body.Height / 2;
        var flatEnd = body.Right - radius;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(body.TopLeft, true);
        ctx.LineTo(new Point(flatEnd, body.Top));
        ctx.ArcTo(new Point(flatEnd, body.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        ctx.LineTo(body.BottomLeft);
        ctx.EndFigure(true);
        return geometry;
    }

    private static Geometry OrShape(Rect body, double shift)
    {
        var left = body.Left + shift;
        var depth = body.Height * OrBackDepthShare;
        var tip = new Point(body.Right, body.Center.Y);
        var control = left + (body.Right - left) * 0.55;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(left, body.Top), true);
        ctx.QuadraticBezierTo(new Point(control, body.Top), tip);
        ctx.QuadraticBezierTo(new Point(control, body.Bottom), new Point(left, body.Bottom));
        ctx.QuadraticBezierTo(new Point(left + depth, body.Center.Y), new Point(left, body.Top));
        ctx.EndFigure(true);
        return geometry;
    }

    /// <summary>The extra curved back line of XOR/XNOR, drawn <paramref name="shift"/> left of the body.</summary>
    private static Geometry OrBack(Rect body, double shift)
    {
        var left = body.Left + shift + body.Height * XorGapShare;
        var depth = body.Height * OrBackDepthShare;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(left, body.Top), false);
        ctx.QuadraticBezierTo(new Point(left + depth, body.Center.Y), new Point(left, body.Bottom));
        ctx.EndFigure(false);
        return geometry;
    }

    private static Geometry Triangle(Rect body)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(body.TopLeft, true);
        ctx.LineTo(new Point(body.Right, body.Center.Y));
        ctx.LineTo(body.BottomLeft);
        ctx.EndFigure(true);
        return geometry;
    }

    /// <summary>The clock-input wedge on a register's left edge.</summary>
    private static Geometry ClockMark(Rect body)
    {
        var size = body.Height * ClockShare;
        var baseY = body.Bottom - size * 0.6;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(body.Left, baseY - size / 2), false);
        ctx.LineTo(new Point(body.Left + size, baseY));
        ctx.LineTo(new Point(body.Left, baseY + size / 2));
        ctx.EndFigure(false);
        return geometry;
    }
}
