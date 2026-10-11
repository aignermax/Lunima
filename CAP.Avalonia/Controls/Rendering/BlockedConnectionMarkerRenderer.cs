using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CAP_Core.Routing;
using PathSegment = CAP_Core.Routing.PathSegment;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Warning markers for blocked / fallback connections: a danger ring at each pin and a round
/// "!" badge at the middle of the drawn path, all screen-constant so they read at any zoom.
/// Markers are collected while connections draw and painted in a separate pass after the
/// components, so no component body can hide them. A marker shown for the first time pulses
/// briefly (<see cref="CanvasMotion.BlockedPulse"/>) and then stays still.
/// </summary>
public sealed class BlockedConnectionMarkerRenderer
{
    private const double BadgeRadiusPx = 7.5;
    private const double PinRingRadiusPx = 5.0;
    private const double PinRingStrokePx = 1.75;
    private const double PulseGrowthPx = 16.0;
    private const byte PulseAlpha = 0xA0;

    private static readonly IBrush BadgeGlyphBrush = CanvasPalette.Brush(CanvasPalette.Gray950);
    private static readonly IBrush BadgeShadowBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray950, 0xB0));

    private readonly List<Marker> _pending = new();

    /// <summary>One blocked connection's marker positions (world coordinates).</summary>
    private readonly record struct Marker(Guid ConnectionId, Point Start, Point End, Point Middle);

    /// <summary>Markers collected this frame (test seam).</summary>
    internal int PendingCount => _pending.Count;

    /// <summary>Queues the markers of one blocked connection.</summary>
    public void Collect(Guid connectionId, Point start, Point end, IReadOnlyList<PathSegment> drawnSegments)
        => _pending.Add(new Marker(connectionId, start, end, PathMiddle(start, end, drawnSegments)));

    /// <summary>Draws and clears the queued markers. Call after the component pass.</summary>
    public void Flush(DrawingContext context, CanvasRenderContext rc)
    {
        double zoom = rc.Zoom <= 0 ? 1.0 : rc.Zoom;
        foreach (var marker in _pending)
        {
            var ringPen = rc.Pens.ScreenPen(CanvasPalette.Danger, PinRingStrokePx);
            double ring = PinRingRadiusPx / zoom;
            context.DrawEllipse(null, ringPen, marker.Start, ring, ring);
            context.DrawEllipse(null, ringPen, marker.End, ring, ring);

            if (rc.Motion?.BlockedPulse(marker.ConnectionId) is { } phase)
                DrawPulse(context, marker.Middle, phase, zoom);
            DrawBadge(context, marker.Middle, zoom);
        }
        _pending.Clear();
    }

    /// <summary>Midpoint of the middle drawn segment, or of the straight pin-to-pin line.</summary>
    internal static Point PathMiddle(Point start, Point end, IReadOnlyList<PathSegment> segments)
    {
        if (segments.Count == 0)
            return new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        var middle = segments[segments.Count / 2];
        if (middle is BendSegment bend)
            return BendMidpoint(bend);
        return new Point((middle.StartPoint.X + middle.EndPoint.X) / 2, (middle.StartPoint.Y + middle.EndPoint.Y) / 2);
    }

    private static Point BendMidpoint(BendSegment bend)
    {
        double sign = Math.Sign(bend.SweepAngleDegrees) == 0 ? 1 : Math.Sign(bend.SweepAngleDegrees);
        double angle = (bend.StartAngleDegrees + bend.SweepAngleDegrees / 2) * Math.PI / 180 - Math.PI / 2 * sign;
        return new Point(bend.Center.X + bend.RadiusMicrometers * Math.Cos(angle),
            bend.Center.Y + bend.RadiusMicrometers * Math.Sin(angle));
    }

    private static void DrawPulse(DrawingContext context, Point center, double phase, double zoom)
    {
        double eased = 1 - (1 - phase) * (1 - phase);
        double radius = (BadgeRadiusPx + PulseGrowthPx * eased) / zoom;
        var pen = new ImmutablePen(new ImmutableSolidColorBrush(
            CanvasPalette.WithAlpha(CanvasPalette.Danger, (byte)(PulseAlpha * (1 - phase)))), 2.0 / zoom);
        context.DrawEllipse(null, pen, center, radius, radius);
    }

    /// <summary>Danger disc with a dark "!" — bar and dot built from rectangles, no text shaping.</summary>
    private static void DrawBadge(DrawingContext context, Point center, double zoom)
    {
        double r = BadgeRadiusPx / zoom;
        context.DrawEllipse(BadgeShadowBrush, null, center, r + 1.5 / zoom, r + 1.5 / zoom);
        context.DrawEllipse(CanvasPalette.DangerBrush, null, center, r, r);

        double barWidth = r * 0.26;
        double corner = barWidth / 2;
        var bar = new Rect(center.X - barWidth / 2, center.Y - r * 0.56, barWidth, r * 0.7);
        context.DrawRectangle(BadgeGlyphBrush, null, bar, corner, corner);
        double dot = barWidth * 0.62;
        context.DrawEllipse(BadgeGlyphBrush, null, new Point(center.X, center.Y + r * 0.42), dot, dot);
    }
}
