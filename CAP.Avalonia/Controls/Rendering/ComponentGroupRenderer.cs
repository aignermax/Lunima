using Avalonia;
using Avalonia.Media;
using CAP_Core.Components.Core;
using System.Globalization;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders ComponentGroups as simple bounding boxes with labels.
/// Groups are rendered with dashed borders, internal components visible,
/// and the group name displayed above.
/// </summary>
public static class ComponentGroupRenderer
{
    private static readonly Color BorderColor = Color.FromRgb(100, 149, 237); // CornflowerBlue
    private static readonly Color HoverBorderColor = Color.FromRgb(65, 105, 225); // RoyalBlue
    private static readonly Color SelectedBorderColor = Color.FromRgb(0, 255, 255); // Cyan
    private static readonly Color GroupHoverOverlay = Color.FromArgb(51, 255, 165, 0); // Orange 20%

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

        return new Rect(
            minX - BorderPadding,
            minY - BorderPadding,
            maxX - minX + 2 * BorderPadding,
            maxY - minY + 2 * BorderPadding
        );
    }

    /// <summary>
    /// Renders a dashed border around the group bounds.
    /// </summary>
    public static void RenderGroupBorder(DrawingContext context, Rect bounds, bool isHovered)
    {
        var borderColor = isHovered ? HoverBorderColor : BorderColor;
        var dashedPen = new Pen(new SolidColorBrush(borderColor), BorderThickness)
        {
            DashStyle = new DashStyle(new[] { 4.0, 4.0 }, 0)
        };

        context.DrawRectangle(null, dashedPen, bounds);
    }

    /// <summary>
    /// Renders a solid selection border around the group bounds.
    /// </summary>
    public static void RenderGroupSelectionBorder(DrawingContext context, Rect bounds)
    {
        var selectionPen = new Pen(new SolidColorBrush(SelectedBorderColor), BorderThickness + 1);
        context.DrawRectangle(null, selectionPen, bounds);
    }

    /// <summary>
    /// Renders the group name label at the top-left of the group bounds.
    /// </summary>
    public static void RenderGroupNameLabel(
        DrawingContext context, Rect bounds, string groupName, bool isLabelHovered)
    {
        var labelText = new FormattedText(
            groupName,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            Brushes.White
        );

        double labelWidth = labelText.Width + 8;
        double labelHeight = 18;
        var labelBg = new Rect(bounds.X, bounds.Y - 20, labelWidth, labelHeight);

        var bgColor = isLabelHovered ? HoverBorderColor : BorderColor;
        context.FillRectangle(new SolidColorBrush(bgColor), labelBg);

        if (isLabelHovered)
        {
            var hoverBorderPen = new Pen(Brushes.White, 1);
            context.DrawRectangle(null, hoverBorderPen, labelBg);
        }

        context.DrawText(labelText, new Point(bounds.X + 4, bounds.Y - 18));
    }

    /// <summary>
    /// Calculates the label bounds for a ComponentGroup (used for hit testing).
    /// </summary>
    public static Rect CalculateLabelBounds(ComponentGroup group)
    {
        var groupBounds = CalculateGroupBounds(group);

        double labelWidth = Math.Max(60, group.GroupName.Length * 7 + 8);
        double labelHeight = 18;

        return new Rect(groupBounds.X, groupBounds.Y - 20, labelWidth, labelHeight);
    }

    /// <summary>
    /// Renders a hover overlay on a component group (gold/orange tint).
    /// </summary>
    public static void RenderGroupHoverOverlay(
        DrawingContext context, double x, double y, double width, double height)
    {
        var rect = new Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(GroupHoverOverlay), rect);
    }
}
