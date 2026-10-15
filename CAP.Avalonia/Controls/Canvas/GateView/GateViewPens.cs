using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Logic.GateView;

namespace CAP.Avalonia.Controls.Canvas.GateView;

/// <summary>
/// Screen-constant pens and brushes of the gate view for one zoom level. Net colours match
/// the Logic tab and the canvas 0/1 badges: green for 1, dim grey for 0; nets without an
/// evaluated level are drawn in a neutral light grey, dashed, so "not run yet" never reads
/// as 0.
/// </summary>
internal sealed class GateViewPens
{
    private const double SymbolStrokePx = 1.5;
    private const double SelectedStrokePx = 2.5;
    private const double NetStrokePx = 1.6;
    private const double OneStrokePx = 2.2;
    private const double BlockStrokePx = 1.0;
    private const double DashPx = 5.0;
    private const double TypicalBodyHeight = 150.0;
    private const double DetailMinBodyPx = 14.0;

    /// <summary>Symbol body fill.</summary>
    public static readonly IBrush BodyFill = CanvasPalette.Brush(CanvasPalette.ComponentBody);

    /// <summary>Fill of a selected symbol.</summary>
    public static readonly IBrush SelectedFill = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Accent, 0x40));

    /// <summary>Net colour for logic 1.</summary>
    public static readonly IBrush OneBrush = CanvasPalette.SuccessBrush;

    /// <summary>Net colour for logic 0.</summary>
    public static readonly IBrush ZeroBrush = CanvasPalette.Brush(CanvasPalette.Gray400);

    /// <summary>Net colour while no level is known.</summary>
    public static readonly IBrush UnknownBrush = CanvasPalette.Brush(CanvasPalette.Gray300);

    private GateViewPens(double zoom)
    {
        var scale = 1 / (zoom <= 0 ? 1 : zoom);
        Symbol = new Pen(CanvasPalette.TextSecondaryBrush, SymbolStrokePx * scale);
        Hovered = new Pen(CanvasPalette.AccentHoverBrush, SymbolStrokePx * scale);
        Selected = new Pen(CanvasPalette.AccentBrush, SelectedStrokePx * scale);
        One = new Pen(OneBrush, OneStrokePx * scale);
        Zero = new Pen(ZeroBrush, NetStrokePx * scale);
        // Dashes only read once gates are big enough to follow a wire; an overview stays calm.
        Unknown = IsDetailed(zoom)
            ? new Pen(UnknownBrush, NetStrokePx * scale, new DashStyle([DashPx, DashPx * 0.6], 0))
            : new Pen(UnknownBrush, NetStrokePx * scale);
        Block = new Pen(CanvasPalette.TextTertiaryBrush, BlockStrokePx * scale, new DashStyle([DashPx, DashPx], 0));
        Scale = scale;
    }

    /// <summary>True once a typical gate symbol is large enough on screen for full detail.</summary>
    public static bool IsDetailed(double zoom) => TypicalBodyHeight * zoom >= DetailMinBodyPx;

    /// <summary>World units per screen pixel.</summary>
    public double Scale { get; }

    /// <summary>Symbol outline.</summary>
    public IPen Symbol { get; }

    /// <summary>Hovered symbol outline.</summary>
    public IPen Hovered { get; }

    /// <summary>Selected symbol outline.</summary>
    public IPen Selected { get; }

    /// <summary>Net at logic 1.</summary>
    public IPen One { get; }

    /// <summary>Net at logic 0.</summary>
    public IPen Zero { get; }

    /// <summary>Net without a known level.</summary>
    public IPen Unknown { get; }

    /// <summary>Outline of a non-gate block.</summary>
    public IPen Block { get; }

    /// <summary>The pen of a net level.</summary>
    public IPen ForLevel(GateNetLevel level) => level switch
    {
        GateNetLevel.One => One,
        GateNetLevel.Zero => Zero,
        _ => Unknown,
    };

    /// <summary>The brush of a net level (junction dots, flags, labels).</summary>
    public static IBrush BrushFor(GateNetLevel level) => level switch
    {
        GateNetLevel.One => OneBrush,
        GateNetLevel.Zero => ZeroBrush,
        _ => UnknownBrush,
    };

    /// <summary>Pens for one frame.</summary>
    public static GateViewPens For(double zoom) => new(zoom);
}
