using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Visualization;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.PowerFlow;
using CAP_Core.Routing;
using System.Globalization;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders ComponentGroups with IPKISS-style transparent hierarchy.
/// Groups are rendered with dashed borders, internal components visible,
/// and external pins highlighted.
/// </summary>
public static class ComponentGroupRenderer
{
    private static readonly IBrush GroupHoverOverlayBrush = CanvasPalette.AccentSubtleBrush;
    private static readonly IBrush ExternalPinBrush = CanvasPalette.Brush(CanvasPalette.PinOptical);
    private static readonly IBrush ExternalPinHoverBrush = CanvasPalette.Brush(CanvasPalette.Gray100);
    private static readonly IBrush ExternalPinGlowBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Accent, 0x55));
    private static readonly IBrush LabelBackgroundBrush = CanvasPalette.SurfaceBrush;
    private static readonly IBrush LabelHoverBackgroundBrush = CanvasPalette.Brush(CanvasPalette.Gray700);
    private static readonly IBrush LabelTextBrush = CanvasPalette.TextSecondaryBrush;
    private static readonly IBrush LabelHoverTextBrush = CanvasPalette.TextPrimaryBrush;
    private static readonly IBrush KeyholeBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray950, 0xC8));
    private static readonly Pen ExternalPinRimPen = new(CanvasPalette.Brush(CanvasPalette.Gray950), 1.5);
    private static readonly Pen LabelBorderPen = new(CanvasPalette.SurfaceBorderBrush, 1);
    private static readonly Pen LabelHoverBorderPen = new(CanvasPalette.Brush(CanvasPalette.Gray400), 1);
    private static readonly Pen PinDirectionPen = new(CanvasPalette.Brush(CanvasPalette.PinDirection), 1);
    private static readonly Pen PinDirectionHoverPen = new(CanvasPalette.AccentHoverBrush, 2);

    private const double DefaultPinSize = 8.0;
    private const double HoveredPinSize = 12.0;
    private const double BorderPadding = 10.0;

    /// <summary>
    /// Calculates the bounding box for a component group with padding.
    /// </summary>
    public static Rect CalculateGroupBounds(ComponentGroup group)
    {
        if (group.ChildComponents.Count == 0)
        {
            return new Rect(group.PhysicalX, group.PhysicalY, 0, 0);
        }

        double minX = group.ChildComponents.Min(c => c.PhysicalX);
        double minY = group.ChildComponents.Min(c => c.PhysicalY);
        double maxX = group.ChildComponents.Max(c => c.PhysicalX + c.WidthMicrometers);
        double maxY = group.ChildComponents.Max(c => c.PhysicalY + c.HeightMicrometers);

        // Add padding around the children
        return new Rect(
            minX - BorderPadding,
            minY - BorderPadding,
            maxX - minX + 2 * BorderPadding,
            maxY - minY + 2 * BorderPadding
        );
    }

    /// <summary>
    /// Renders a frozen waveguide path from a ComponentGroup.
    /// Applies power flow visualization if available.
    /// </summary>
    /// <param name="context">Drawing context.</param>
    /// <param name="frozenPath">The frozen path to render.</param>
    /// <param name="powerFlowResult">Optional power flow result for color rendering.</param>
    /// <param name="fadeThresholdDb">Threshold in dB for fading out weak connections.</param>
    /// <param name="cullRect">Optional world-space cull rectangle: segments fully outside
    /// it are skipped. Long frozen buses can span far beyond the viewport, so per-segment
    /// culling avoids drawing hundreds of off-screen lines; <c>null</c> draws every segment.</param>
    /// <param name="layerVisibility">Per-design layer view filter (issue #858): a path
    /// tagged with its import source (layer, datatype) is skipped when that layer is
    /// hidden and drawn faded when it carries a reduced opacity. Untagged paths and a
    /// <c>null</c> filter always draw fully visible.</param>
    /// <param name="penOverride">Optional pen that wins over both the power-flow pen and
    /// the layer/default pen — used for the selection highlight of canvas-level paths.</param>
    /// <param name="zoom">Canvas zoom, for the level-of-detail cull of drawn polygons.</param>
    public static void RenderFrozenWaveguidePath(
        DrawingContext context,
        FrozenWaveguidePath frozenPath,
        PowerFlowResult? powerFlowResult = null,
        double fadeThresholdDb = -40.0,
        Rect? cullRect = null,
        Services.GdsImport.LayerVisibility.GdsLayerVisibilityState? layerVisibility = null,
        IPen? penOverride = null,
        double zoom = 1.0)
    {
        if (frozenPath?.Path?.Segments == null || frozenPath.Path.Segments.Count == 0)
            return;

        if (frozenPath.AsDrawnGeometry is { } asDrawn && penOverride == null && powerFlowResult == null)
        {
            // An untouched imported path draws exactly as drawn.
            AsDrawnGeometryRenderer.Draw(context, asDrawn, zoom, layerVisibility);
            return;
        }

        double layerOpacity = frozenPath.Layer is int pathLayer && frozenPath.DataType is int pathDataType
            ? layerVisibility?.EffectiveOpacity(pathLayer, pathDataType) ?? 1.0
            : 1.0;
        if (layerOpacity <= 0)
            return;
        if (layerOpacity < 1.0)
        {
            using (context.PushOpacity(layerOpacity))
                RenderFrozenPathBody(context, frozenPath, powerFlowResult, fadeThresholdDb, cullRect, penOverride, zoom);
            return;
        }
        RenderFrozenPathBody(context, frozenPath, powerFlowResult, fadeThresholdDb, cullRect, penOverride, zoom);
    }

    /// <summary>Draws a frozen path's segments and optional power-flow label (the part
    /// of <see cref="RenderFrozenWaveguidePath"/> under the layer-opacity push).</summary>
    private static void RenderFrozenPathBody(
        DrawingContext context,
        FrozenWaveguidePath frozenPath,
        PowerFlowResult? powerFlowResult,
        double fadeThresholdDb,
        Rect? cullRect,
        IPen? penOverride,
        double zoom)
    {
        IPen frozenPen;

        // Use power flow colors if available
        if (penOverride != null)
        {
            frozenPen = penOverride;
        }
        else if (powerFlowResult != null &&
            powerFlowResult.ConnectionFlows.TryGetValue(frozenPath.PathId, out var flow))
        {
            frozenPen = PowerFlowRenderer.CreatePowerPen(flow, fadeThresholdDb);
        }
        else
        {
            frozenPen = SelectFrozenPathPen(frozenPath, zoom);
        }

        foreach (var segment in frozenPath.Path.Segments)
        {
            if (cullRect.HasValue && !cullRect.Value.Intersects(RenderCulling.ComputeSegmentBounds(segment)))
                continue;

            if (segment is StraightSegment straight)
            {
                context.DrawLine(
                    frozenPen,
                    new Point(straight.StartPoint.X, straight.StartPoint.Y),
                    new Point(straight.EndPoint.X, straight.EndPoint.Y)
                );
            }
            else if (segment is BendSegment bend)
            {
                RenderBendSegment(context, frozenPen, bend);
            }
        }

        // Render power loss label at midpoint when power flow visualization is active
        if (powerFlowResult != null)
        {
            powerFlowResult.ConnectionFlows.TryGetValue(frozenPath.PathId, out var labelFlow);
            var midPoint = CalculateFrozenPathMidpoint(frozenPath);
            DrawFrozenPathLabel(context, labelFlow, midPoint);
        }
    }

    /// <summary>
    /// The pen a frozen path draws with when no power-flow overlay is active: a path
    /// tagged with its import source's (layer, datatype) draws in that layer's muted
    /// palette color (same alpha/thickness as the default), everything else draws
    /// like a routed waveguide (<see cref="CanvasPens.FrozenPath"/>). A blocked fallback
    /// draws with the red dashed <see cref="CanvasPens.Blocked"/> pen of a blocked top-level
    /// wire, so a broken route inside a group is as visible as one outside. Internal so unit
    /// tests can assert the selection headlessly (InternalsVisibleTo UnitTests).
    /// </summary>
    internal static IPen SelectFrozenPathPen(FrozenWaveguidePath frozenPath, double zoom = 1.0) =>
        frozenPath.Path?.IsBlockedFallback == true
            ? CanvasPens.For(zoom).Blocked
            : frozenPath.Layer is int layer && frozenPath.DataType is int dataType
                ? OutlineLayerPalette.PathPenFor(layer, dataType)
                : CanvasPens.For(zoom).FrozenPath;

    /// <summary>
    /// Calculates the visual midpoint of a frozen path from its first and last segment endpoints.
    /// </summary>
    private static Point CalculateFrozenPathMidpoint(FrozenWaveguidePath frozenPath)
    {
        var segments = frozenPath.Path.Segments;
        var firstStart = segments[0].StartPoint;
        var lastEnd = segments[^1].EndPoint;
        return new Point(
            (firstStart.X + lastEnd.X) / 2,
            (firstStart.Y + lastEnd.Y) / 2
        );
    }

    /// <summary>
    /// Draws a power loss label at the specified midpoint position.
    /// Shows dB and percentage when signal is present, "no signal" otherwise.
    /// </summary>
    private static void DrawFrozenPathLabel(
        DrawingContext context,
        ConnectionPowerFlow? flow,
        Point midPoint)
    {
        string labelText;
        IBrush labelBrush;

        if (flow != null && flow.AveragePower > 0)
        {
            labelText = $"{flow.NormalizedPowerDb:F1}dB ({flow.NormalizedPowerFraction * 100:F0}%)";
            var fraction = Math.Clamp(flow.NormalizedPowerFraction, 0, 1);
            labelBrush = new SolidColorBrush(PowerFlowRenderer.InterpolatePowerColor(fraction));
        }
        else
        {
            labelText = "no signal";
            labelBrush = CanvasPalette.TextTertiaryBrush;
        }

        var labelPosition = new Point(midPoint.X, midPoint.Y - 15);
        var formatted = new FormattedText(
            labelText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            CanvasTypography.Ui,
            10,
            labelBrush);

        var textBounds = new Rect(
            labelPosition.X - 2,
            labelPosition.Y - 2,
            formatted.Width + 4,
            formatted.Height + 4);

        context.DrawRectangle(CanvasPalette.LabelBackplateBrush, null, textBounds, 2, 2);
        context.DrawText(formatted, labelPosition);
    }

    /// <summary>
    /// Renders a bend segment as a series of line segments approximating the arc.
    /// </summary>
    private static void RenderBendSegment(DrawingContext context, IPen pen, BendSegment bend)
    {
        int numSegments = Math.Max(8, (int)(Math.Abs(bend.SweepAngleDegrees) / 5));

        if (numSegments == 0 || Math.Abs(bend.SweepAngleDegrees) < 0.1)
        {
            context.DrawLine(
                pen,
                new Point(bend.StartPoint.X, bend.StartPoint.Y),
                new Point(bend.EndPoint.X, bend.EndPoint.Y)
            );
            return;
        }

        var points = new List<Point>();

        for (int i = 0; i <= numSegments; i++)
        {
            double t = i / (double)numSegments;
            double startRad = bend.StartAngleDegrees * Math.PI / 180;
            double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
            double angle = startRad + sweepRad * t;

            double sign = Math.Sign(bend.SweepAngleDegrees);
            if (sign == 0) sign = 1;
            double perpAngle = angle - Math.PI / 2 * sign;

            double x = bend.Center.X + bend.RadiusMicrometers * Math.Cos(perpAngle);
            double y = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(perpAngle);
            points.Add(new Point(x, y));
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            context.DrawLine(pen, points[i], points[i + 1]);
        }
    }

    /// <summary>
    /// Renders the dashed hairline outline of an unselected group (lighter under the pointer).
    /// </summary>
    public static void RenderGroupBorder(DrawingContext context, Rect bounds, bool isHovered, CanvasPens pens)
    {
        context.DrawRectangle(null, isHovered ? pens.GroupBorderHover : pens.GroupBorder, bounds);
    }

    /// <summary>
    /// Renders the selection outline of a group: accent border with a soft glow that fades in
    /// over <see cref="CanvasMotion.SelectionFadeMs"/> (settled when no motion clock is attached).
    /// </summary>
    public static void RenderGroupSelectionBorder(DrawingContext context, Rect bounds, CanvasRenderContext rc, Guid groupId = default)
    {
        double glow = rc.Motion?.SelectionGlow(groupId) ?? 1.0;
        double zoom = rc.Zoom <= 0 ? 1.0 : rc.Zoom;
        if (glow > 0)
        {
            double thickness = SelectionGlowPx / zoom;
            var glowPen = rc.Pens.ScreenPen(CanvasPalette.WithAlpha(CanvasPalette.Accent, (byte)(SelectionGlowAlpha * glow)), SelectionGlowPx);
            context.DrawRectangle(null, glowPen, bounds.Inflate(thickness / 2));
        }
        context.DrawRectangle(null, rc.Pens.Selection, bounds);
    }

    private const double SelectionGlowPx = 5.0;
    private const byte SelectionGlowAlpha = 0x40;

    /// <summary>
    /// Renders the group name label at the top-left of the group bounds.
    /// </summary>
    public static void RenderGroupNameLabel(DrawingContext context, Rect bounds, string groupName, bool isDimmed = false)
    {
        RenderGroupNameLabel(context, bounds, groupName, isDimmed, false);
    }

    /// <summary>
    /// Renders the group name tag above the top-left corner of the group bounds: a dark rounded
    /// chip with a hairline border, brightened under the pointer to show it is grabbable. Below
    /// 100 % zoom the tag grows with <see cref="LabelScale"/> so it stays legible — the group is
    /// the readable unit of an overview — and is left out once it would be wider than its group.
    /// Geometry matches <see cref="CalculateLabelBounds"/> exactly, so hit testing follows.
    /// </summary>
    public static void RenderGroupNameLabel(DrawingContext context, Rect bounds, string groupName, bool isDimmed,
        bool isLabelHovered, double zoom = 1.0)
    {
        double scale = LabelScale(zoom);
        if (!IsLabelShown(bounds, groupName, scale))
            return;

        var labelText = CreateLabelText(groupName, isLabelHovered ? LabelHoverTextBrush : LabelTextBrush);

        var labelBg = new Rect(bounds.X, bounds.Y - LabelOffset, labelText.Width + 10, LabelHeight);
        var transform = Matrix.CreateTranslation(-bounds.X, -bounds.Y) * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(bounds.X, bounds.Y);

        using (context.PushTransform(transform))
        using (isDimmed ? context.PushOpacity(0.5) : (IDisposable?)null)
        {
            context.DrawRectangle(isLabelHovered ? LabelHoverBackgroundBrush : LabelBackgroundBrush,
                isLabelHovered ? LabelHoverBorderPen : LabelBorderPen, labelBg, 4, 4);
            context.DrawText(labelText, new Point(bounds.X + 5, labelBg.Y + (LabelHeight - labelText.Height) / 2));
        }
    }

    private const double LabelFontSize = 11;

    private static FormattedText CreateLabelText(string groupName, IBrush foreground) =>
        new(groupName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            CanvasTypography.UiMedium, LabelFontSize, foreground);
    private const double LabelHeight = 18;
    private const double LabelOffset = 20;

    /// <summary>Lowest on-screen scale of the group tag; below it the tag is enlarged in world units.</summary>
    private const double LabelMinScreenScale = 0.9;

    /// <summary>
    /// World-space enlargement of the group name tag at <paramref name="zoom"/>: 1 at and above
    /// <see cref="LabelMinScreenScale"/>, growing as the view zooms out so the tag keeps a
    /// legible on-screen size.
    /// </summary>
    public static double LabelScale(double zoom) =>
        zoom <= 0 ? 1.0 : Math.Max(1.0, LabelMinScreenScale / zoom);

    private static double EstimateLabelWidth(string groupName) => Math.Max(60, groupName.Length * 7 + 8);

    private static bool IsLabelShown(Rect groupBounds, string groupName, double scale) =>
        scale <= 1.0 || EstimateLabelWidth(groupName) * scale <= groupBounds.Width;

    /// <summary>
    /// Calculates the actual label bounds for a ComponentGroup (used for hit testing).
    /// Returns a rectangle representing the clickable label area.
    /// </summary>
    public static Rect CalculateLabelBounds(ComponentGroup group, double zoom = 1.0)
    {
        var groupBounds = CalculateGroupBounds(group);
        double scale = LabelScale(zoom);
        if (!IsLabelShown(groupBounds, group.GroupName, scale))
            return default;

        // Estimate label width based on text (approximate 7 pixels per character + padding)
        double labelWidth = EstimateLabelWidth(group.GroupName) * scale;
        return new Rect(groupBounds.X, groupBounds.Y - LabelOffset * scale, labelWidth, LabelHeight * scale);
    }

    /// <summary>
    /// Renders an external pin for a ComponentGroup.
    /// The hover pin name is enqueued into <paramref name="labels"/> (deferred topmost pass)
    /// rather than drawn inline, like every other pin name.
    /// </summary>
    public static void RenderExternalPin(DrawingContext context, GroupPin pin, ComponentGroup group, bool isHovered, DeferredLabelLayer labels)
    {
        double pinX = group.PhysicalX + pin.RelativeX;
        double pinY = group.PhysicalY + pin.RelativeY;
        double pinSize = isHovered ? HoveredPinSize : DefaultPinSize;

        context.DrawEllipse(isHovered ? ExternalPinHoverBrush : ExternalPinBrush, ExternalPinRimPen,
            new Point(pinX, pinY), pinSize / 2, pinSize / 2);

        if (isHovered)
            EnqueuePinName(labels, pin.Name, new Point(pinX + 8, pinY - 5));
    }

    /// <summary>
    /// Renders an unoccupied external pin for a ComponentGroup outside edit mode.
    /// These pins are available for creating external connections.
    /// </summary>
    /// <param name="context">Drawing context.</param>
    /// <param name="pin">The group pin to render.</param>
    /// <param name="group">The parent component group.</param>
    /// <param name="isHovered">Whether the pin is being hovered.</param>
    /// <param name="labels">Deferred topmost label layer receiving the hover pin name
    /// (enqueued, not drawn inline, like every other pin name).</param>
    public static void RenderUnoccupiedGroupPin(DrawingContext context, GroupPin pin, ComponentGroup group, bool isHovered, DeferredLabelLayer labels)
    {
        double pinX = group.PhysicalX + pin.RelativeX;
        double pinY = group.PhysicalY + pin.RelativeY;
        double pinSize = isHovered ? HoveredPinSize : DefaultPinSize;

        if (isHovered)
            context.DrawEllipse(ExternalPinGlowBrush, null, new Point(pinX, pinY), pinSize * 1.5, pinSize * 1.5);

        double angle = pin.AngleDegrees * Math.PI / 180;
        double dirLength = isHovered ? 20 : 15;
        context.DrawLine(isHovered ? PinDirectionHoverPen : PinDirectionPen,
            new Point(pinX, pinY),
            new Point(pinX + Math.Cos(angle) * dirLength, pinY + Math.Sin(angle) * dirLength));

        context.DrawEllipse(isHovered ? ExternalPinHoverBrush : ExternalPinBrush, ExternalPinRimPen,
            new Point(pinX, pinY), pinSize / 2, pinSize / 2);

        if (isHovered)
            EnqueuePinName(labels, pin.Name, new Point(pinX + 15, pinY - 15));
    }

    private static void EnqueuePinName(DeferredLabelLayer labels, string name, Point origin)
    {
        var nameText = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            CanvasTypography.UiMedium, 10, CanvasPalette.AccentHoverBrush);
        labels.Enqueue(nameText, CanvasPalette.AccentHoverBrush, origin);
    }

    /// <summary>
    /// Renders a hover overlay on a component (gold/orange tint).
    /// </summary>
    public static void RenderGroupHoverOverlay(DrawingContext context, double x, double y, double width, double height)
    {
        var rect = new Rect(x, y, width, height);
        context.FillRectangle(GroupHoverOverlayBrush, rect);
    }

    /// <summary>
    /// Calculates the bounds of the lock icon for a ComponentGroup.
    /// Lock icon is positioned at the top-right corner of the group bounds.
    /// </summary>
    public static Rect CalculateLockIconBounds(ComponentGroup group)
    {
        var bounds = CalculateGroupBounds(group);
        const double IconSize = 16.0;
        const double Padding = 4.0;

        // Position at top-right corner
        double iconX = bounds.Right - IconSize - Padding;
        double iconY = bounds.Top + Padding;

        return new Rect(iconX, iconY, IconSize, IconSize);
    }

    /// <summary>
    /// Renders a lock icon for a ComponentGroup using a Material Design-style padlock icon.
    /// Shows a closed padlock (🔒) when locked, open padlock (🔓) when unlocked.
    /// </summary>
    /// <param name="context">Drawing context.</param>
    /// <param name="group">The group to render the lock icon for.</param>
    /// <param name="isHovered">Whether the lock icon is being hovered.</param>
    public static void RenderGroupLockIcon(DrawingContext context, ComponentGroup group, bool isHovered)
    {
        var iconBounds = CalculateLockIconBounds(group);
        const double IconSize = 16.0;
        double scale = isHovered ? 1.125 : 1.0; // Scale up on hover (18px when hovered)
        double scaledSize = IconSize * scale;

        // Center the scaled icon
        double centerX = iconBounds.X + iconBounds.Width / 2;
        double centerY = iconBounds.Y + iconBounds.Height / 2;
        double scaledX = centerX - scaledSize / 2;
        double scaledY = centerY - scaledSize / 2;

        // Draw semi-transparent background circle
        var bgBrush = isHovered ? LabelHoverBackgroundBrush : LabelBackgroundBrush;
        context.DrawEllipse(bgBrush, null, new Point(centerX, centerY), scaledSize / 2, scaledSize / 2);

        // Use red for locked (danger), green for unlocked (safe)
        var lockColor = group.IsLocked ? CanvasPalette.Danger : CanvasPalette.Success;
        if (isHovered)
            lockColor = CanvasPalette.Lighten(lockColor, 0.2);

        var lockBrush = new SolidColorBrush(lockColor);
        var lockStrokePen = new Pen(lockBrush, 1.5);

        if (group.IsLocked)
        {
            RenderClosedPadlock(context, scaledX, scaledY, scaledSize, lockBrush, lockStrokePen);
        }
        else
        {
            RenderOpenPadlock(context, scaledX, scaledY, scaledSize, lockBrush, lockStrokePen);
        }

        // Draw hover border around icon background to indicate interactivity
        if (isHovered)
        {
            context.DrawEllipse(null, LabelHoverBorderPen, new Point(centerX, centerY), scaledSize / 2 + 1, scaledSize / 2 + 1);
        }
    }

    /// <summary>
    /// Renders a closed padlock icon (🔒) using Material Design-style geometry.
    /// </summary>
    private static void RenderClosedPadlock(DrawingContext context, double x, double y, double size, Brush fillBrush, Pen strokePen)
    {
        // Lock body (rounded rectangle)
        double bodyWidth = size * 0.6;
        double bodyHeight = size * 0.5;
        double bodyX = x + (size - bodyWidth) / 2;
        double bodyY = y + size * 0.45;
        double cornerRadius = size * 0.08;

        var bodyRect = new Rect(bodyX, bodyY, bodyWidth, bodyHeight);
        context.DrawRectangle(fillBrush, strokePen, bodyRect, cornerRadius, cornerRadius);

        // Keyhole (small circle in center of body)
        double keyholeRadius = size * 0.08;
        double keyholeX = x + size / 2;
        double keyholeY = bodyY + bodyHeight * 0.4;
        var keyholeBrush = KeyholeBrush;
        context.DrawEllipse(keyholeBrush, null, new Point(keyholeX, keyholeY), keyholeRadius, keyholeRadius);

        // Shackle (closed U-shape)
        double shackleWidth = size * 0.45;
        double shackleHeight = size * 0.35;
        double shackleThickness = size * 0.08;
        double shackleCenterX = x + size / 2;
        double shackleBottomY = bodyY;

        // Draw shackle as a thick arc
        var shackleGeometry = new StreamGeometry();
        using (var ctx = shackleGeometry.Open())
        {
            // Outer arc (top of shackle)
            double outerRadius = shackleWidth / 2;
            double innerRadius = outerRadius - shackleThickness;

            // Left side of shackle (bottom to top)
            ctx.BeginFigure(new Point(shackleCenterX - innerRadius, shackleBottomY), true);
            ctx.LineTo(new Point(shackleCenterX - outerRadius, shackleBottomY));

            // Top arc (outer)
            ctx.ArcTo(
                new Point(shackleCenterX + outerRadius, shackleBottomY),
                new Size(outerRadius, shackleHeight),
                0,
                false,
                SweepDirection.CounterClockwise);

            // Right side down
            ctx.LineTo(new Point(shackleCenterX + innerRadius, shackleBottomY));

            // Top arc (inner) - going back
            ctx.ArcTo(
                new Point(shackleCenterX - innerRadius, shackleBottomY),
                new Size(innerRadius, shackleHeight - shackleThickness),
                0,
                false,
                SweepDirection.Clockwise);
        }

        context.DrawGeometry(fillBrush, strokePen, shackleGeometry);
    }

    /// <summary>
    /// Renders an open padlock icon (🔓) using Material Design-style geometry.
    /// </summary>
    private static void RenderOpenPadlock(DrawingContext context, double x, double y, double size, Brush fillBrush, Pen strokePen)
    {
        // Lock body (rounded rectangle)
        double bodyWidth = size * 0.6;
        double bodyHeight = size * 0.5;
        double bodyX = x + (size - bodyWidth) / 2;
        double bodyY = y + size * 0.45;
        double cornerRadius = size * 0.08;

        var bodyRect = new Rect(bodyX, bodyY, bodyWidth, bodyHeight);
        context.DrawRectangle(fillBrush, strokePen, bodyRect, cornerRadius, cornerRadius);

        // Keyhole (small circle in center of body)
        double keyholeRadius = size * 0.08;
        double keyholeX = x + size / 2;
        double keyholeY = bodyY + bodyHeight * 0.4;
        var keyholeBrush = KeyholeBrush;
        context.DrawEllipse(keyholeBrush, null, new Point(keyholeX, keyholeY), keyholeRadius, keyholeRadius);

        // Shackle (open, rotated to the right)
        double shackleWidth = size * 0.45;
        double shackleHeight = size * 0.35;
        double shackleThickness = size * 0.08;
        double shackleCenterX = x + size / 2 + size * 0.12; // Offset right for open appearance
        double shackleBottomY = bodyY - size * 0.05;

        // Draw open shackle (only left side and top arc, right side is "open")
        var shackleGeometry = new StreamGeometry();
        using (var ctx = shackleGeometry.Open())
        {
            double outerRadius = shackleWidth / 2;
            double innerRadius = outerRadius - shackleThickness;

            // Left vertical post (thick line)
            ctx.BeginFigure(new Point(shackleCenterX - outerRadius, shackleBottomY), true);
            ctx.LineTo(new Point(shackleCenterX - outerRadius, shackleBottomY - shackleHeight));

            // Top arc (outer)
            ctx.ArcTo(
                new Point(shackleCenterX + outerRadius * 0.6, shackleBottomY - shackleHeight * 0.8),
                new Size(outerRadius, shackleHeight * 0.9),
                0,
                false,
                SweepDirection.CounterClockwise);

            // Inner arc back
            ctx.ArcTo(
                new Point(shackleCenterX - innerRadius, shackleBottomY - shackleHeight + shackleThickness),
                new Size(innerRadius, shackleHeight * 0.7),
                0,
                false,
                SweepDirection.Clockwise);

            // Close left post
            ctx.LineTo(new Point(shackleCenterX - innerRadius, shackleBottomY));
        }

        context.DrawGeometry(fillBrush, strokePen, shackleGeometry);
    }
}
