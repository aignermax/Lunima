using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Zoom-dependent pens for the canvas. Strokes that are UI affordances (borders, selection,
/// grid frame) stay a constant number of screen pixels; strokes that stand for physical
/// geometry (waveguides, metal traces) keep their world width but never get thinner than a
/// legible hairline when zoomed out. The set is rebuilt only when the zoom changes, so the
/// render loop reuses the same pens for every component and connection of a frame.
/// </summary>
public sealed class CanvasPens
{
    /// <summary>World width of an optical waveguide stroke at high zoom.</summary>
    public const double WaveguideWorldWidth = 1.5;

    /// <summary>World width of a metal trace: markedly thicker than a waveguide (#682).</summary>
    public const double ElectricalWorldWidth = 5.0;

    private const double HairlinePx = 1.0;
    private const double WaveguideMinPx = 1.4;
    private const double ElectricalMinPx = 2.5;
    private const double SelectionPx = 1.5;
    private const double SelectedWaveguideMinPx = 2.5;
    private const double HoverBoostPx = 1.5;
    private const double BlockedMinPx = 2.0;

    private static CanvasPens? _current;

    private CanvasPens(double zoom)
    {
        Zoom = zoom <= 0 ? 1.0 : zoom;
        ComponentBorder = Solid(CanvasPalette.ComponentBorder, HairlinePx);
        ComponentBorderHover = Solid(CanvasPalette.Gray400, HairlinePx);
        Selection = Solid(CanvasPalette.Accent, SelectionPx);
        PinDirection = Solid(CanvasPalette.PinDirection, HairlinePx);

        Waveguide = Geometry(CanvasPalette.Waveguide, WaveguideWorldWidth, WaveguideMinPx);
        WaveguideHover = Geometry(CanvasPalette.Lighten(CanvasPalette.Waveguide, 0.45), WaveguideWorldWidth, WaveguideMinPx + HoverBoostPx);
        WaveguideSelected = Geometry(CanvasPalette.Accent, WaveguideWorldWidth, SelectedWaveguideMinPx);
        Electrical = Geometry(CanvasPalette.Copper, ElectricalWorldWidth, ElectricalMinPx);
        ElectricalHover = Geometry(CanvasPalette.Lighten(CanvasPalette.Copper, 0.4), ElectricalWorldWidth, ElectricalMinPx + HoverBoostPx);
        Blocked = Dashed(CanvasPalette.Danger, Math.Max(2.0, BlockedMinPx / Zoom));
        BlockedHover = Dashed(CanvasPalette.Lighten(CanvasPalette.Danger, 0.35), Math.Max(2.0, (BlockedMinPx + HoverBoostPx) / Zoom));
        NoFlow = new ImmutablePen(new ImmutableSolidColorBrush(CanvasPalette.WithAlpha(CanvasPalette.Gray500, 0x50)), HairlinePx / Zoom);
        FrozenPath = Geometry(CanvasPalette.WithAlpha(CanvasPalette.Waveguide, 0xD0), WaveguideWorldWidth, WaveguideMinPx);

        GroupBorder = DashedPx(CanvasPalette.Gray500, HairlinePx);
        GroupBorderHover = DashedPx(CanvasPalette.Gray300, HairlinePx);
        ChipBorder = Solid(CanvasPalette.Gray600, HairlinePx);
        ChipCorner = Solid(CanvasPalette.Gray400, SelectionPx);
    }

    /// <summary>The zoom this set was built for.</summary>
    public double Zoom { get; }

    /// <summary>Component bounding-box border at rest.</summary>
    public IPen ComponentBorder { get; }
    /// <summary>Component border under the pointer.</summary>
    public IPen ComponentBorderHover { get; }
    /// <summary>Selection outline (components and groups).</summary>
    public IPen Selection { get; }
    /// <summary>Pin direction stub.</summary>
    public IPen PinDirection { get; }
    /// <summary>Optical waveguide connection.</summary>
    public IPen Waveguide { get; }
    /// <summary>Optical waveguide under the pointer.</summary>
    public IPen WaveguideHover { get; }
    /// <summary>Selected connection.</summary>
    public IPen WaveguideSelected { get; }
    /// <summary>Metal trace.</summary>
    public IPen Electrical { get; }
    /// <summary>Metal trace under the pointer.</summary>
    public IPen ElectricalHover { get; }
    /// <summary>Blocked / unroutable connection: danger, dashed.</summary>
    public IPen Blocked { get; }
    /// <summary>Blocked connection under the pointer.</summary>
    public IPen BlockedHover { get; }
    /// <summary>Connection without simulated power while the power-flow view is on.</summary>
    public IPen NoFlow { get; }
    /// <summary>Frozen waveguide path inside a group.</summary>
    public IPen FrozenPath { get; }
    /// <summary>Group outline at rest.</summary>
    public IPen GroupBorder { get; }
    /// <summary>Group outline under the pointer.</summary>
    public IPen GroupBorderHover { get; }
    /// <summary>Chip boundary.</summary>
    public IPen ChipBorder { get; }
    /// <summary>Chip corner ticks.</summary>
    public IPen ChipCorner { get; }

    /// <summary>The pen set for <paramref name="zoom"/>; reused until the zoom changes.</summary>
    public static CanvasPens For(double zoom)
    {
        var current = _current;
        double safeZoom = zoom <= 0 ? 1.0 : zoom;
        if (current != null && current.Zoom == safeZoom)
            return current;
        current = new CanvasPens(safeZoom);
        _current = current;
        return current;
    }

    /// <summary>A screen-constant solid pen of <paramref name="px"/> pixels at this zoom.</summary>
    public IPen ScreenPen(Color color, double px) => Solid(color, px);

    private IPen Solid(Color color, double px) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color), px / Zoom);

    private IPen Geometry(Color color, double worldWidth, double minPx) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color), Math.Max(worldWidth, minPx / Zoom),
            lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static IPen Dashed(Color color, double thickness) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color), thickness,
            new ImmutableDashStyle(new[] { 3.0, 2.0 }, 0), PenLineCap.Flat, PenLineJoin.Round);

    private IPen DashedPx(Color color, double px) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color), px / Zoom,
            new ImmutableDashStyle(new[] { 4.0, 3.0 }, 0));
}
