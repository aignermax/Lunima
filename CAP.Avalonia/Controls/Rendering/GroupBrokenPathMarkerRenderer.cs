using Avalonia;
using Avalonia.Media;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Marks a group that holds a broken frozen path (blocked fallback or invalid bend
/// geometry) at any nesting depth, so the breakage is visible from the top level without
/// entering the group: the group outline turns into the red dashed "blocked" stroke that
/// top-level blocked wires use, and a small red "!" badge sits on its top-right corner.
/// </summary>
public static class GroupBrokenPathMarkerRenderer
{
    private const double BadgeRadiusPx = 7.0;
    private const double BadgeFontPx = 10.0;
    private static readonly IBrush BadgeTextBrush = CanvasPalette.Brush(CanvasPalette.Gray950);

    /// <summary>
    /// True when the group needs the marker — delegates to
    /// <see cref="FrozenBlockedPathChecker.ContainsBrokenPath"/>, the same verdict the
    /// Design Checks report per path.
    /// </summary>
    public static bool NeedsMarker(ComponentGroup group) => FrozenBlockedPathChecker.ContainsBrokenPath(group);

    /// <summary>Draws the red outline and the corner badge around <paramref name="bounds"/>.</summary>
    /// <param name="context">The drawing context (world coordinates).</param>
    /// <param name="bounds">The group's outline rectangle.</param>
    /// <param name="pens">The zoom-dependent pen set of the frame.</param>
    public static void Render(DrawingContext context, Rect bounds, CanvasPens pens)
    {
        if (pens.Zoom < ComponentRenderer.GroupOverviewZoom)
            return;
        context.DrawRectangle(null, pens.Blocked, bounds);

        double zoom = pens.Zoom <= 0 ? 1.0 : pens.Zoom;
        double radius = BadgeRadiusPx / zoom;
        var center = new Point(bounds.Right, bounds.Top);
        context.DrawEllipse(CanvasPalette.DangerBrush, null, center, radius, radius);

        var text = new FormattedText("!", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, CanvasTypography.UiMedium, BadgeFontPx / zoom, BadgeTextBrush);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}
