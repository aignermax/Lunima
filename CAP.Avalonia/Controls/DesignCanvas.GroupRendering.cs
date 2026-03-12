using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;
using System.Globalization;

namespace CAP.Avalonia.Controls;

/// <summary>
/// ComponentGroup rendering methods for DesignCanvas.
/// Implements IPKISS-style transparent hierarchy rendering.
/// </summary>
public partial class DesignCanvas
{
    /// <summary>
    /// Color constants for group rendering.
    /// </summary>
    private static class GroupRenderingColors
    {
        public static readonly Color BorderDefault = Color.FromRgb(100, 149, 237); // CornflowerBlue
        public static readonly Color BorderHovered = Color.FromRgb(65, 105, 225); // RoyalBlue (brighter)
        public static readonly Color LabelBackground = Color.FromRgb(100, 149, 237); // CornflowerBlue
        public static readonly Color GroupHoverOverlay = Color.FromArgb(51, 255, 165, 0); // Gold with 20% opacity
    }

    /// <summary>
    /// Renders all component groups on the canvas.
    /// Called after regular components are rendered.
    /// </summary>
    private void DrawComponentGroups(DrawingContext context, DesignCanvasViewModel vm)
    {
        var groupInstances = vm.GetAllGroupInstances();

        foreach (var groupInstance in groupInstances)
        {
            bool isHovered = _interactionState.HoveredGroupInstance?.InstanceId == groupInstance.InstanceId;
            DrawComponentGroup(context, groupInstance, isHovered);
        }
    }

    /// <summary>
    /// Renders a single component group with dashed border and label.
    /// </summary>
    private void DrawComponentGroup(
        DrawingContext context,
        ComponentGroupInstance groupInstance,
        bool isHovered)
    {
        // Calculate padded bounds
        var (x, y, width, height) = ComponentGroupRenderer.CalculatePaddedBounds(groupInstance, 10.0);

        // Choose border color based on hover state
        var borderColor = isHovered ? GroupRenderingColors.BorderHovered : GroupRenderingColors.BorderDefault;

        // Create dashed pen for border
        var dashedPen = new Pen(new SolidColorBrush(borderColor), 2)
        {
            DashStyle = new DashStyle(new double[] { 4.0, 4.0 }, 0)
        };

        // Draw dashed border
        var borderRect = new Rect(x, y, width, height);
        context.DrawRectangle(null, dashedPen, borderRect);

        // Draw group name label at top-left
        DrawGroupNameLabel(context, groupInstance.Name, x, y);
    }

    /// <summary>
    /// Draws the group name label at the top-left of the group bounds.
    /// </summary>
    private void DrawGroupNameLabel(DrawingContext context, string groupName, double x, double y)
    {
        const double LabelOffsetY = 20.0;
        const double LabelPaddingX = 4.0;
        const double LabelPaddingY = 2.0;

        var labelText = new FormattedText(
            groupName,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            Brushes.White);

        // Draw background rectangle
        var labelBgRect = new Rect(
            x,
            y - LabelOffsetY,
            labelText.Width + 2 * LabelPaddingX,
            18);
        context.FillRectangle(new SolidColorBrush(GroupRenderingColors.LabelBackground), labelBgRect);

        // Draw text
        context.DrawText(labelText, new Point(x + LabelPaddingX, y - LabelOffsetY + LabelPaddingY));
    }

    /// <summary>
    /// Draws hover overlay on components that belong to the hovered group.
    /// </summary>
    private void DrawGroupHoverOverlay(DrawingContext context, ComponentViewModel comp)
    {
        var vm = ViewModel;
        if (vm == null || _interactionState.HoveredGroupInstance == null)
            return;

        // Check if this component belongs to the hovered group
        if (comp.Component.ParentGroupInstanceId != _interactionState.HoveredGroupInstance.InstanceId)
            return;

        // Draw semi-transparent gold overlay
        var overlayBrush = new SolidColorBrush(GroupRenderingColors.GroupHoverOverlay);
        var rect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);
        context.FillRectangle(overlayBrush, rect);
    }
}
