using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Controls.Rendering.DeviceCategories;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Draws the body of a non-imported component: a faintly category-tinted rounded footprint with
/// a crisp one-pixel border and a thin category-colored top edge, the GDS preview on top when it
/// is available (the geometry stays the hero), and the selection glow / hover border. Far out
/// (see <see cref="RenderCulling.IsBelowLodThreshold"/>) the body collapses to a solid block in
/// its category color, so the overview of a large design reads as a map of device classes.
/// </summary>
internal static class ComponentBodyRenderer
{
    /// <summary>Corner radius in world units (~2 µm); capped by the footprint so tiny parts stay boxes.</summary>
    private const double CornerRadiusWorld = 2.0;
    private const double MaxCornerFraction = 0.18;

    /// <summary>Below this on-screen radius rounding is invisible, so the cheaper plain rectangle is drawn.</summary>
    private const double MinVisibleRadiusPx = 0.75;
    private const double AccentEdgePx = 2.0;
    private const double AccentEdgeMaxFraction = 0.25;
    private const double AccentEdgeMinScreenPx = 14.0;
    private const double GlowOuterPx = 9.0;
    private const double GlowInnerPx = 4.5;
    private const byte GlowOuterAlpha = 0x26;
    private const byte GlowInnerAlpha = 0x4C;

    /// <summary>Visual state of a component body.</summary>
    internal readonly record struct BodyState(bool IsSelected, bool IsHovered, double GlowProgress);

    /// <summary>Draws the full body (fill, preview, accent edge, border, selection).</summary>
    public static void Draw(DrawingContext context, Component component, Rect rect, BodyState state,
        CanvasRenderContext rc, GdsPreviewData? preview)
    {
        var brushes = DeviceCategoryPalette.For(DeviceCategoryClassifier.Classify(component));
        if (RenderCulling.IsBelowLodThreshold(rect.Width, rect.Height, rc.Zoom))
        {
            context.FillRectangle(state.IsSelected ? CanvasPalette.AccentBrush : brushes.Lod, rect);
            return;
        }

        double radius = Math.Min(CornerRadiusWorld, MaxCornerFraction * Math.Min(rect.Width, rect.Height));
        if (radius * rc.Zoom < MinVisibleRadiusPx)
            radius = 0;
        if (state.IsSelected)
            DrawGlow(context, rect, radius, rc.Zoom, state.GlowProgress);

        context.DrawRectangle(preview != null ? brushes.BodyUnderPreview : brushes.Body, null, rect, radius, radius);
        if (preview != null)
            GdsPolygonRenderer.DrawGdsPreview(context, preview, component, rect, rc.Zoom);
        else if (state.IsSelected)
            context.DrawRectangle(CanvasPalette.AccentSubtleBrush, null, rect, radius, radius);

        DrawAccentEdge(context, rect, radius, rc.Zoom, brushes.Accent);
        context.DrawRectangle(null, BorderPen(state, rc.Pens), rect, radius, radius);
    }

    /// <summary>Outline for a component whose body is drawn elsewhere (imported GDS outlines).</summary>
    public static void DrawOutlineOnly(DrawingContext context, Rect rect, BodyState state, CanvasRenderContext rc)
    {
        if (state.IsSelected)
            DrawGlow(context, rect, 0, rc.Zoom, state.GlowProgress);
        context.DrawRectangle(null, BorderPen(state, rc.Pens), rect);
    }

    private static IPen BorderPen(BodyState state, CanvasPens pens) =>
        state.IsSelected ? pens.Selection : state.IsHovered ? pens.ComponentBorderHover : pens.ComponentBorder;

    private static void DrawAccentEdge(DrawingContext context, Rect rect, double radius, double zoom, IBrush accent)
    {
        if (Math.Max(rect.Width, rect.Height) * zoom < AccentEdgeMinScreenPx)
            return;
        double height = Math.Min(AccentEdgePx / zoom, rect.Height * AccentEdgeMaxFraction);
        double width = rect.Width - 2 * radius;
        if (width <= 0 || height <= 0)
            return;
        context.FillRectangle(accent, new Rect(rect.X + radius, rect.Y, width, height));
    }

    /// <summary>Soft accent halo around a selected footprint, faded in by <paramref name="progress"/>.</summary>
    private static void DrawGlow(DrawingContext context, Rect rect, double radius, double zoom, double progress)
    {
        if (progress <= 0)
            return;
        DrawGlowRing(context, rect, radius, GlowOuterPx / zoom, (byte)(GlowOuterAlpha * progress));
        DrawGlowRing(context, rect, radius, GlowInnerPx / zoom, (byte)(GlowInnerAlpha * progress));
    }

    private static void DrawGlowRing(DrawingContext context, Rect rect, double radius, double thickness, byte alpha)
    {
        var pen = new ImmutablePen(new ImmutableSolidColorBrush(CanvasPalette.WithAlpha(CanvasPalette.Accent, alpha)), thickness);
        double half = thickness / 2;
        context.DrawRectangle(null, pen, rect.Inflate(half), radius + half, radius + half);
    }
}
