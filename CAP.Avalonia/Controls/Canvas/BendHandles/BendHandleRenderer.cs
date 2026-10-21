using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;

namespace CAP.Avalonia.Controls.Canvas.BendHandles;

/// <summary>
/// Draws Figma-style in-canvas bend-radius handles for the selected waveguide connection
/// (issue #574). Runs in the world transform; all handle sizes are divided by
/// <see cref="CanvasRenderContext.Zoom"/> so they stay screen-constant. Metal traces take
/// the same handles as waveguides — RF metal bends are editable too (#854). The selected connection comes
/// from <c>MainViewModel.CanvasInteraction.SelectedWaveguideConnection</c>; the active/clamped
/// handle state comes from <see cref="CanvasInteractionState"/>.
/// </summary>
public sealed class BendHandleRenderer : ICanvasRenderer
{
    private const double HandleRadiusPx = 6.0;
    private const double HandleStrokePx = 1.5;
    private const double LabelFontPx = 11.0;
    private const double LabelOffsetPx = 10.0;

    private static readonly IBrush HandleFill = CanvasPalette.AccentBrush;
    private static readonly IBrush ActiveFill = CanvasPalette.AccentHoverBrush;
    private static readonly IBrush ClampFill = CanvasPalette.DangerBrush;
    private static readonly IBrush HandleStrokeBrush = CanvasPalette.HandleStrokeBrush;
    private static readonly IBrush LabelBrush = CanvasPalette.TextPrimaryBrush;
    private static readonly Typeface LabelTypeface = CanvasTypography.UiMedium;

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        var selected = rc.MainViewModel?.CanvasInteraction.SelectedWaveguideConnection;
        if (selected == null || selected.Connection.AsDrawnGeometry is not null)
            return;

        double zoom = rc.Zoom <= 0 ? 1.0 : rc.Zoom;
        DrawHandles(context, selected, rc.InteractionState, zoom);
    }

    private static void DrawHandles(DrawingContext context, WaveguideConnectionViewModel conn,
                                    CanvasInteractionState state, double zoom)
    {
        var corners = BendRadiusEditor.GetBendCorners(conn.Connection.GetPathSegments());
        double radius = HandleRadiusPx / zoom;
        var stroke = new Pen(HandleStrokeBrush, HandleStrokePx / zoom);

        foreach (var corner in corners)
        {
            var (hx, hy) = BendHandleGeometry.HandlePoint(corner);
            var fill = SelectFill(corner.BendIndex, state);
            context.DrawEllipse(fill, stroke, new Point(hx, hy), radius, radius);
            DrawLabel(context, corner, hx, hy, zoom);
        }
    }

    private static IBrush SelectFill(int bendIndex, CanvasInteractionState state)
    {
        if (bendIndex != state.ActiveBendIndex)
            return HandleFill;
        return state.ActiveBendClamped ? ClampFill : ActiveFill;
    }

    private static void DrawLabel(DrawingContext context, BendCorner corner, double hx, double hy, double zoom)
    {
        // Numeric label uses the UI culture (a display string); the unit "µm" is allow-listed.
        string text = string.Create(CultureInfo.CurrentCulture, $"{corner.RadiusMicrometers:F0} µm");
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            LabelTypeface, LabelFontPx / zoom, LabelBrush);
        double offset = (HandleRadiusPx + LabelOffsetPx) / zoom;
        double originX = hx + offset * corner.Bisector.X;
        double originY = hy + offset * corner.Bisector.Y;

        // DrawText renders from the text's TOP-LEFT corner, so for a bisector pointing left/up
        // the box would grow back over the handle circle and hide the value. Anchor the side of
        // the text box that faces the handle instead, so it always grows away from it.
        if (corner.Bisector.X < 0)
            originX -= formatted.Width;
        if (corner.Bisector.Y < 0)
            originY -= formatted.Height;

        context.DrawText(formatted, new Point(originX, originY));
    }
}
