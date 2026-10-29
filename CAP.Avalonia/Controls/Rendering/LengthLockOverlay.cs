using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Canvas;
using PathSegment = CAP_Core.Routing.PathSegment;
using StraightSegment = CAP_Core.Routing.StraightSegment;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Canvas cues for length-locked routes (a recorded length target): a small padlock on the
/// route, and — while one of them is selected or hovered — a glow on every route locked to the
/// same length, so the routes that were equalized together are visible as one group.
/// </summary>
internal static class LengthLockOverlay
{
    /// <summary>Targets closer than this many decimals (µm) count as the same lock group.</summary>
    private const int TargetDecimals = 3;
    /// <summary>On-screen size of the padlock badge.</summary>
    private const double BadgeScreenPx = 14;
    private static readonly IBrush BadgeBackdrop = CanvasPalette.SurfaceBrush;

    /// <summary>Lock groups to emphasize this frame: those of the selected and the hovered routes.</summary>
    public static IReadOnlySet<double> ActiveGroups(DesignCanvasViewModel vm, WaveguideConnectionViewModel? hovered)
    {
        var groups = new HashSet<double>();
        foreach (var connection in vm.Connections.Where(c => c.IsSelected).Append(hovered))
        {
            if (connection != null && GroupOf(connection) is { } group)
                groups.Add(group);
        }
        return groups;
    }

    /// <summary>The lock group of a route, or null when it has no length target.</summary>
    public static double? GroupOf(WaveguideConnectionViewModel connection) =>
        connection.Connection.TargetLengthMicrometers is { } target ? Math.Round(target, TargetDecimals) : null;

    /// <summary>Draws the padlock at the middle of the route.</summary>
    public static void DrawBadge(DrawingContext context, IReadOnlyList<PathSegment> segments, double zoom)
    {
        if (segments.Count == 0)
            return;
        var (x, y) = Midpoint(segments);
        double size = BadgeScreenPx / zoom;
        DrawPadlock(context, x - size / 2, y - size / 2, size);
    }

    /// <summary>A padlock glyph in a round backdrop, top-left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void DrawPadlock(DrawingContext context, double x, double y, double size)
    {
        context.DrawEllipse(BadgeBackdrop, null, new Point(x + size / 2, y + size / 2), size / 2, size / 2);

        double bodyWidth = size * 0.5;
        double bodyHeight = size * 0.4;
        double bodyX = x + (size - bodyWidth) / 2;
        double bodyY = y + size * 0.5;
        double corner = bodyWidth * 0.15;
        context.DrawRectangle(CanvasPalette.WarningBrush, null, new Rect(bodyX, bodyY, bodyWidth, bodyHeight), corner, corner);

        double shackleWidth = size * 0.34;
        double shackleHeight = size * 0.24;
        double shackleCenterX = x + size / 2;
        var shackle = new StreamGeometry();
        using (var ctx = shackle.Open())
        {
            ctx.BeginFigure(new Point(shackleCenterX - shackleWidth / 2, bodyY), false);
            ctx.ArcTo(new Point(shackleCenterX + shackleWidth / 2, bodyY),
                new Size(shackleWidth / 2, shackleHeight), 0, false, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, new Pen(CanvasPalette.WarningBrush, size * 0.09), shackle);
    }

    /// <summary>The point halfway along the route (a segment end is close enough inside a bend).</summary>
    private static (double X, double Y) Midpoint(IReadOnlyList<PathSegment> segments)
    {
        double half = segments.Sum(s => s.LengthMicrometers) / 2;
        foreach (var segment in segments)
        {
            double length = segment.LengthMicrometers;
            if (half <= length && segment is StraightSegment && length > 0)
            {
                double t = half / length;
                return (segment.StartPoint.X + (segment.EndPoint.X - segment.StartPoint.X) * t,
                    segment.StartPoint.Y + (segment.EndPoint.Y - segment.StartPoint.Y) * t);
            }
            if (half <= length)
                return segment.EndPoint;
            half -= length;
        }
        return segments[^1].EndPoint;
    }
}
