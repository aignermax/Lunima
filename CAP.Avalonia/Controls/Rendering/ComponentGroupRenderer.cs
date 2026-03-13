using Avalonia;
using Avalonia.Media;
using CAP_Core.Components.Core;
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
    /// </summary>
    public static void RenderFrozenWaveguidePath(DrawingContext context, FrozenWaveguidePath frozenPath)
    {
        if (frozenPath?.Path?.Segments == null || frozenPath.Path.Segments.Count == 0)
            return;

        // Use a distinct color for frozen paths (slightly dimmed from normal connections)
        var frozenPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 140, 0)), 2);

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
    /// Renders the group name label at the top-left of the group bounds.
    /// </summary>
    public static void RenderGroupNameLabel(DrawingContext context, Rect bounds, string groupName, bool isDimmed = false)
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

        var bgColor = Color.FromArgb(alpha, BorderColor.R, BorderColor.G, BorderColor.B);
        context.FillRectangle(new SolidColorBrush(bgColor), labelBg);
        context.DrawText(labelText, new Point(bounds.X + 4, bounds.Y - 18));
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
    /// Renders a hover overlay on a component (gold/orange tint).
    /// </summary>
    public static void RenderGroupHoverOverlay(DrawingContext context, double x, double y, double width, double height)
    {
        var rect = new Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(GroupHoverOverlay), rect);
    }
}
