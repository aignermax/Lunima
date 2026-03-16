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
    private static readonly Color BorderColor = Color.FromRgb(100, 149, 237); // CornflowerBlue #6495ED
    private static readonly Color HoverBorderColor = Color.FromRgb(65, 105, 225); // RoyalBlue #4169E1
    private static readonly Color SelectedBorderColor = Color.FromRgb(0, 255, 255); // Cyan #00FFFF
    private static readonly Color ExternalPinColor = Color.FromRgb(144, 238, 144); // LightGreen
    private static readonly Color ExternalPinHoverColor = Color.FromRgb(255, 215, 0); // Gold
    private static readonly Color GroupHoverOverlay = Color.FromArgb(51, 255, 165, 0); // Orange overlay (20% opacity)

    private const double DefaultPinSize = 8.0;
    private const double HoveredPinSize = 12.0;
    private const double BorderThickness = 2.0;
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
    public static void RenderFrozenWaveguidePath(
        DrawingContext context,
        FrozenWaveguidePath frozenPath,
        PowerFlowResult? powerFlowResult = null,
        double fadeThresholdDb = -40.0)
    {
        if (frozenPath?.Path?.Segments == null || frozenPath.Path.Segments.Count == 0)
            return;

        Pen frozenPen;

        // Use power flow colors if available
        if (powerFlowResult != null &&
            powerFlowResult.ConnectionFlows.TryGetValue(frozenPath.PathId, out var flow))
        {
            frozenPen = PowerFlowRenderer.CreatePowerPen(flow, fadeThresholdDb);
        }
        else
        {
            // Default color for frozen paths (orange, slightly dimmed)
            frozenPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 140, 0)), 2);
        }

        foreach (var segment in frozenPath.Path.Segments)
        {
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
    }

    /// <summary>
    /// Renders a bend segment as a series of line segments approximating the arc.
    /// </summary>
    private static void RenderBendSegment(DrawingContext context, Pen pen, BendSegment bend)
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
    /// Renders a dashed border around the group bounds.
    /// </summary>
    public static void RenderGroupBorder(DrawingContext context, Rect bounds, bool isHovered, bool isDimmed = false)
    {
        byte alpha = (byte)(isDimmed ? 128 : 255);
        var borderColor = isHovered ? HoverBorderColor : BorderColor;
        var dimmedBorderColor = Color.FromArgb(alpha, borderColor.R, borderColor.G, borderColor.B);
        var dashedPen = new Pen(new SolidColorBrush(dimmedBorderColor), BorderThickness)
        {
            DashStyle = new DashStyle(new[] { 4.0, 4.0 }, 0)
        };

        context.DrawRectangle(null, dashedPen, bounds);
    }

    /// <summary>
    /// Renders a solid selection border around the group bounds.
    /// Used to indicate that the group is selected (distinct from hover).
    /// </summary>
    public static void RenderGroupSelectionBorder(DrawingContext context, Rect bounds, bool isDimmed = false)
    {
        byte alpha = (byte)(isDimmed ? 128 : 255);
        var selectionColor = Color.FromArgb(alpha, SelectedBorderColor.R, SelectedBorderColor.G, SelectedBorderColor.B);
        var selectionPen = new Pen(new SolidColorBrush(selectionColor), BorderThickness + 1);

        context.DrawRectangle(null, selectionPen, bounds);
    }

    /// <summary>
    /// Renders the group name label at the top-left of the group bounds.
    /// </summary>
    public static void RenderGroupNameLabel(DrawingContext context, Rect bounds, string groupName, bool isDimmed = false)
    {
        RenderGroupNameLabel(context, bounds, groupName, isDimmed, false);
    }

    /// <summary>
    /// Renders the group name label at the top-left of the group bounds with optional hover state.
    /// </summary>
    public static void RenderGroupNameLabel(DrawingContext context, Rect bounds, string groupName, bool isDimmed, bool isLabelHovered)
    {
        byte alpha = (byte)(isDimmed ? 128 : 255);
        var labelText = new FormattedText(
            groupName,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255))
        );

        double labelWidth = labelText.Width + 8;
        double labelHeight = 18;
        var labelBg = new Rect(bounds.X, bounds.Y - 20, labelWidth, labelHeight);

        // Use hover color if label is being hovered
        var bgColor = isLabelHovered
            ? Color.FromArgb(alpha, HoverBorderColor.R, HoverBorderColor.G, HoverBorderColor.B)
            : Color.FromArgb(alpha, BorderColor.R, BorderColor.G, BorderColor.B);
        context.FillRectangle(new SolidColorBrush(bgColor), labelBg);

        // Add subtle border on hover to indicate interactivity
        if (isLabelHovered)
        {
            var hoverBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), 1);
            context.DrawRectangle(null, hoverBorderPen, labelBg);
        }

        context.DrawText(labelText, new Point(bounds.X + 4, bounds.Y - 18));
    }

    /// <summary>
    /// Calculates the actual label bounds for a ComponentGroup (used for hit testing).
    /// Returns a rectangle representing the clickable label area.
    /// </summary>
    public static Rect CalculateLabelBounds(ComponentGroup group)
    {
        var groupBounds = CalculateGroupBounds(group);

        // Estimate label width based on text (approximate 7 pixels per character + padding)
        double labelWidth = Math.Max(60, group.GroupName.Length * 7 + 8);
        double labelHeight = 18;

        return new Rect(groupBounds.X, groupBounds.Y - 20, labelWidth, labelHeight);
    }

    /// <summary>
    /// Renders an external pin for a ComponentGroup.
    /// </summary>
    public static void RenderExternalPin(DrawingContext context, GroupPin pin, ComponentGroup group, bool isHovered)
    {
        // External pins are positioned relative to the group origin
        double pinX = group.PhysicalX + pin.RelativeX;
        double pinY = group.PhysicalY + pin.RelativeY;

        double pinSize = isHovered ? HoveredPinSize : DefaultPinSize;
        var pinColor = isHovered ? ExternalPinHoverColor : ExternalPinColor;

        // Draw circle for pin
        context.DrawEllipse(
            new SolidColorBrush(pinColor),
            new Pen(Brushes.White, 2),
            new Point(pinX, pinY),
            pinSize / 2,
            pinSize / 2
        );

        // Draw pin name on hover
        if (isHovered)
        {
            var nameText = new FormattedText(
                pin.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                10,
                Brushes.White
            );

            context.DrawText(nameText, new Point(pinX + 8, pinY - 5));
        }
    }

    /// <summary>
    /// Renders an unoccupied external pin for a ComponentGroup outside edit mode.
    /// These pins are available for creating external connections.
    /// </summary>
    /// <param name="context">Drawing context.</param>
    /// <param name="pin">The group pin to render.</param>
    /// <param name="group">The parent component group.</param>
    /// <param name="isHovered">Whether the pin is being hovered.</param>
    public static void RenderUnoccupiedGroupPin(DrawingContext context, GroupPin pin, ComponentGroup group, bool isHovered)
    {
        // Calculate absolute pin position
        double pinX = group.PhysicalX + pin.RelativeX;
        double pinY = group.PhysicalY + pin.RelativeY;

        double pinSize = isHovered ? HoveredPinSize : DefaultPinSize;
        var pinColor = isHovered ? ExternalPinHoverColor : ExternalPinColor;

        // Draw outer glow for visibility
        if (isHovered)
        {
            var glowBrush = new SolidColorBrush(Color.FromArgb(100, pinColor.R, pinColor.G, pinColor.B));
            context.DrawEllipse(glowBrush, null, new Point(pinX, pinY), pinSize * 1.5, pinSize * 1.5);
        }

        // Draw main pin circle
        context.DrawEllipse(
            new SolidColorBrush(pinColor),
            new Pen(Brushes.White, 2),
            new Point(pinX, pinY),
            pinSize / 2,
            pinSize / 2
        );

        // Draw direction indicator (small line showing pin orientation)
        double angle = pin.AngleDegrees * Math.PI / 180;
        double dirLength = isHovered ? 20 : 15;
        var dirPen = new Pen(
            new SolidColorBrush(isHovered ? Color.FromRgb(255, 255, 255) : Color.FromRgb(200, 200, 200)),
            isHovered ? 2 : 1
        );

        context.DrawLine(
            dirPen,
            new Point(pinX, pinY),
            new Point(pinX + Math.Cos(angle) * dirLength, pinY + Math.Sin(angle) * dirLength)
        );

        // Draw pin name on hover
        if (isHovered)
        {
            var nameText = new FormattedText(
                pin.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                10,
                Brushes.White
            );

            context.DrawText(nameText, new Point(pinX + 15, pinY - 15));
        }
    }

    /// <summary>
    /// Renders a hover overlay on a component (gold/orange tint).
    /// </summary>
    public static void RenderGroupHoverOverlay(DrawingContext context, double x, double y, double width, double height)
    {
        var rect = new Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(GroupHoverOverlay), rect);
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
    /// Renders a lock icon for a ComponentGroup.
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
        var bgColor = isHovered ? Color.FromArgb(220, 60, 60, 60) : Color.FromArgb(180, 40, 40, 40);
        var bgBrush = new SolidColorBrush(bgColor);
        context.DrawEllipse(bgBrush, null, new Point(centerX, centerY), scaledSize / 2, scaledSize / 2);

        // Draw lock icon shape
        var lockColor = isHovered ? Color.FromRgb(255, 215, 0) : Color.FromRgb(255, 165, 0); // Gold on hover, orange otherwise
        var lockBrush = new SolidColorBrush(lockColor);
        var lockPen = new Pen(lockBrush, 1.5);

        if (group.IsLocked)
        {
            // Draw closed lock (🔒)
            // Lock body (filled rectangle)
            double bodyWidth = scaledSize * 0.5;
            double bodyHeight = scaledSize * 0.5;
            double bodyX = scaledX + (scaledSize - bodyWidth) / 2;
            double bodyY = scaledY + scaledSize * 0.5;
            var bodyRect = new Rect(bodyX, bodyY, bodyWidth, bodyHeight);
            context.DrawRectangle(lockBrush, null, bodyRect);

            // Lock shackle (closed arc)
            double shackleWidth = scaledSize * 0.4;
            double shackleHeight = scaledSize * 0.3;
            double shackleCenterX = centerX;
            double shackleTopY = bodyY;

            var shackleGeometry = new StreamGeometry();
            using (var ctx = shackleGeometry.Open())
            {
                ctx.BeginFigure(new Point(shackleCenterX - shackleWidth / 2, shackleTopY), false);
                ctx.ArcTo(
                    new Point(shackleCenterX + shackleWidth / 2, shackleTopY),
                    new Size(shackleWidth / 2, shackleHeight),
                    0,
                    false,
                    SweepDirection.CounterClockwise);
            }
            context.DrawGeometry(null, lockPen, shackleGeometry);
        }
        else
        {
            // Draw open lock (🔓)
            // Lock body (filled rectangle)
            double bodyWidth = scaledSize * 0.5;
            double bodyHeight = scaledSize * 0.5;
            double bodyX = scaledX + (scaledSize - bodyWidth) / 2;
            double bodyY = scaledY + scaledSize * 0.5;
            var bodyRect = new Rect(bodyX, bodyY, bodyWidth, bodyHeight);
            context.DrawRectangle(lockBrush, null, bodyRect);

            // Lock shackle (open, offset to the right)
            double shackleWidth = scaledSize * 0.4;
            double shackleHeight = scaledSize * 0.35;
            double shackleCenterX = centerX + scaledSize * 0.15; // Offset right for open look
            double shackleTopY = bodyY - shackleHeight * 0.3;

            var shackleGeometry = new StreamGeometry();
            using (var ctx = shackleGeometry.Open())
            {
                ctx.BeginFigure(new Point(shackleCenterX - shackleWidth / 2, shackleTopY + shackleHeight), false);
                ctx.ArcTo(
                    new Point(shackleCenterX + shackleWidth / 2, shackleTopY),
                    new Size(shackleWidth / 2, shackleHeight),
                    0,
                    false,
                    SweepDirection.CounterClockwise);
            }
            context.DrawGeometry(null, lockPen, shackleGeometry);
        }

        // Draw hover border around icon background to indicate interactivity
        if (isHovered)
        {
            var hoverBorderPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 255, 255)), 1);
            context.DrawEllipse(null, hoverBorderPen, new Point(centerX, centerY), scaledSize / 2 + 1, scaledSize / 2 + 1);
        }
    }
}
