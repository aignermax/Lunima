using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.Visualization;
using CAP_Core.Routing;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Component and connection rendering methods for DesignCanvas.
/// </summary>
public partial class DesignCanvas
{
    private void DrawComponent(DrawingContext context, ComponentViewModel comp)
    {
        var vm = ViewModel;
        bool isDimmed = vm != null && IsComponentDimmedInEditMode(comp, vm);

        // If this is a ComponentGroup, use specialized group rendering
        if (comp.Component is ComponentGroup group)
        {
            DrawComponentGroup(context, group, comp.IsSelected, isDimmed);
            return;
        }

        var rect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);

        // Apply dimming if in edit mode and component is external
        byte alpha = (byte)(isDimmed ? 128 : 255);
        var fillBrush = comp.IsSelected
            ? new SolidColorBrush(Color.FromArgb(alpha, 60, 80, 120))
            : new SolidColorBrush(Color.FromArgb(alpha, 40, 50, 70));
        context.FillRectangle(fillBrush, rect);

        var borderPen = comp.IsSelected
            ? new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0, 255, 255)), 2)
            : new Pen(new SolidColorBrush(Color.FromArgb(alpha, 128, 128, 128)), 1);
        context.DrawRectangle(borderPen, rect);

        DrawComponentPins(context, comp, isDimmed);
        DrawComponentName(context, comp, isDimmed);

        // Draw lock icon for locked components
        if (comp.IsLocked)
        {
            DrawLockIcon(context, comp);
        }
    }

    /// <summary>
    /// Checks if a component should be dimmed in edit mode (outside current edit group).
    /// </summary>
    private bool IsComponentDimmedInEditMode(ComponentViewModel comp, DesignCanvasViewModel vm)
    {
        if (!vm.IsInGroupEditMode || vm.CurrentEditGroup == null)
            return false;

        // Component should be dimmed if it's NOT inside the current edit group
        return !IsComponentInGroup(comp.Component, vm.CurrentEditGroup);
    }

    /// <summary>
    /// Checks if a component is a direct child of the given group.
    /// </summary>
    private bool IsComponentInGroup(Component component, ComponentGroup group)
    {
        return group.ChildComponents.Contains(component);
    }

    /// <summary>
    /// Renders a ComponentGroup with IPKISS-style transparent hierarchy.
    /// Shows internal structure, dashed border, and external pins.
    /// </summary>
    private void DrawComponentGroup(DrawingContext context, ComponentGroup group, bool isSelected, bool isDimmed = false)
    {
        var vm = ViewModel;
        bool isHovered = _interactionState.HoveredGroup == group;
        bool isCurrentEditGroup = vm?.CurrentEditGroup == group;

        // 1. Render all child components recursively (transparent hierarchy)
        byte alpha = (byte)(isDimmed ? 128 : 255);
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nestedGroup)
            {
                // Recursively render nested groups
                DrawComponentGroup(context, nestedGroup, isSelected, isDimmed);
            }
            else
            {
                // Render normal components
                var childRect = new Rect(child.PhysicalX, child.PhysicalY,
                    child.WidthMicrometers, child.HeightMicrometers);

                // Apply hover overlay if group is hovered
                if (isHovered)
                {
                    ComponentGroupRenderer.RenderGroupHoverOverlay(
                        context, child.PhysicalX, child.PhysicalY,
                        child.WidthMicrometers, child.HeightMicrometers);
                }

                var fillBrush = new SolidColorBrush(Color.FromArgb(alpha, 40, 50, 70));
                context.FillRectangle(fillBrush, childRect);

                var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 128, 128, 128)), 1);
                context.DrawRectangle(borderPen, childRect);

                // Draw child component name
                var nameText = new FormattedText(
                    child.Identifier,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    10,
                    new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)));
                context.DrawText(nameText, new Point(child.PhysicalX + 3, child.PhysicalY + 3));
            }
        }

        // 2. Render frozen internal waveguide paths
        foreach (var frozenPath in group.InternalPaths)
        {
            ComponentGroupRenderer.RenderFrozenWaveguidePath(context, frozenPath);
        }

        // 3. Draw dashed border around group bounds (hide if this is the current edit group)
        var bounds = ComponentGroupRenderer.CalculateGroupBounds(group);
        if (!isCurrentEditGroup)
        {
            ComponentGroupRenderer.RenderGroupBorder(context, bounds, isHovered, isDimmed);

            // 4. Draw group name label at top-left
            ComponentGroupRenderer.RenderGroupNameLabel(context, bounds, group.GroupName, isDimmed);
        }

        // 5. Draw external pins with distinct style
        foreach (var externalPin in group.ExternalPins)
        {
            ComponentGroupRenderer.RenderExternalPin(context, externalPin, group, isHovered);
        }
    }

    /// <summary>
    /// Draws a lock icon overlay for locked components.
    /// </summary>
    private void DrawLockIcon(DrawingContext context, ComponentViewModel comp)
    {
        // Position lock icon in bottom-right corner of component
        double iconSize = Math.Min(comp.Width, comp.Height) * 0.25;
        iconSize = Math.Clamp(iconSize, 12, 24);

        double iconX = comp.X + comp.Width - iconSize - 4;
        double iconY = comp.Y + comp.Height - iconSize - 4;

        // Draw semi-transparent background circle
        var bgBrush = new SolidColorBrush(Color.FromArgb(180, 40, 40, 40));
        context.DrawEllipse(bgBrush, null, new Point(iconX + iconSize / 2, iconY + iconSize / 2), iconSize / 2, iconSize / 2);

        // Draw lock icon (simplified padlock shape)
        var lockPen = new Pen(Brushes.Orange, 2);

        // Lock body (rectangle)
        double bodyWidth = iconSize * 0.5;
        double bodyHeight = iconSize * 0.5;
        double bodyX = iconX + (iconSize - bodyWidth) / 2;
        double bodyY = iconY + iconSize * 0.5;
        var bodyRect = new Rect(bodyX, bodyY, bodyWidth, bodyHeight);
        context.DrawRectangle(Brushes.Orange, null, bodyRect);

        // Lock shackle (arc)
        double shackleWidth = iconSize * 0.4;
        double shackleHeight = iconSize * 0.3;
        double shackleCenterX = iconX + iconSize / 2;
        double shackleTopY = bodyY;

        // Draw shackle as a small arc (simplified)
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

    private void DrawComponentPins(DrawingContext context, ComponentViewModel comp, bool isDimmed = false)
    {
        var mainVm = MainViewModel;
        bool isConnectMode = mainVm?.CurrentMode == InteractionMode.Connect;
        var highlightedPin = ViewModel?.HighlightedPin?.Pin;
        byte alpha = (byte)(isDimmed ? 128 : 255);

        foreach (var pin in comp.Component.PhysicalPins)
        {
            var (pinX, pinY) = pin.GetAbsolutePosition();
            bool isHighlighted = pin == highlightedPin;

            IBrush pinBrush;
            double pinSize = isConnectMode ? 8 : 5;

            if (isHighlighted)
            {
                pinBrush = new SolidColorBrush(Color.FromArgb(alpha, 0, 255, 255));
                pinSize = 12;
            }
            else if (isConnectMode)
            {
                pinBrush = new SolidColorBrush(Color.FromArgb(alpha, 255, 200, 0));
            }
            else if (pin.LogicalPin != null)
            {
                pinBrush = new SolidColorBrush(Color.FromArgb(alpha, 100, 200, 100));
            }
            else
            {
                pinBrush = new SolidColorBrush(Color.FromArgb(alpha, 200, 100, 100));
            }

            if (isHighlighted)
            {
                var glowBrush = new SolidColorBrush(Color.FromArgb((byte)(100 * alpha / 255), 0, 255, 255));
                context.DrawEllipse(glowBrush, null, new Point(pinX, pinY), pinSize * 1.5, pinSize * 1.5);
            }

            context.DrawEllipse(pinBrush, null, new Point(pinX, pinY), pinSize, pinSize);

            DrawPinDirectionIndicator(context, pin, pinX, pinY, isHighlighted, isDimmed);

            if (isHighlighted)
            {
                var pinText = new FormattedText(
                    pin.Name,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    10,
                    new SolidColorBrush(Color.FromArgb(alpha, 0, 255, 255)));
                context.DrawText(pinText, new Point(pinX + 15, pinY - 15));
            }
        }
    }

    private void DrawPinDirectionIndicator(DrawingContext context, PhysicalPin pin, double pinX, double pinY, bool isHighlighted, bool isDimmed = false)
    {
        byte alpha = (byte)(isDimmed ? 128 : 255);
        var dirBrush = isHighlighted
            ? new SolidColorBrush(Color.FromArgb(alpha, 0, 255, 255))
            : new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
        var dirPen = new Pen(dirBrush, isHighlighted ? 2 : 1);
        double angle = pin.GetAbsoluteAngle() * Math.PI / 180;
        double dirLength = isHighlighted ? 20 : 15;
        context.DrawLine(dirPen,
            new Point(pinX, pinY),
            new Point(pinX + Math.Cos(angle) * dirLength, pinY + Math.Sin(angle) * dirLength));
    }

    private void DrawComponentName(DrawingContext context, ComponentViewModel comp, bool isDimmed = false)
    {
        byte alpha = (byte)(isDimmed ? 128 : 255);
        var text = new FormattedText(
            comp.Name,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)));

        context.DrawText(text, new Point(comp.X + 5, comp.Y + 5));
    }

    private void DrawWaveguideConnection(
        DrawingContext context,
        WaveguideConnectionViewModel conn,
        DesignCanvasViewModel vm)
    {
        var segments = conn.Connection.GetPathSegments();

        Pen waveguidePen = CreateWaveguidePen(conn, vm);

        bool pathIsStale = IsPathStale(segments, conn);

        if (segments.Count == 0 || pathIsStale)
        {
            context.DrawLine(waveguidePen,
                new Point(conn.StartX, conn.StartY),
                new Point(conn.EndX, conn.EndY));
            return;
        }

        DrawPathSegments(context, waveguidePen, segments);
        DrawConnectionLabel(context, conn, vm);
    }

    private Pen CreateWaveguidePen(WaveguideConnectionViewModel conn, DesignCanvasViewModel vm)
    {
        if (conn.IsSelected)
        {
            return new Pen(Brushes.Yellow, 3);
        }
        else if (vm.ShowPowerFlow && vm.PowerFlowVisualizer.CurrentResult != null)
        {
            var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
            return flow != null
                ? PowerFlowRenderer.CreatePowerPen(flow, vm.PowerFlowVisualizer.FadeThresholdDb)
                : new Pen(new SolidColorBrush(Color.FromArgb(40, 80, 80, 120)), 1);
        }
        else if (conn.IsBlockedFallback || conn.Connection.RoutedPath?.IsInvalidGeometry == true)
        {
            return new Pen(Brushes.Red, 2) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };
        }
        else
        {
            return new Pen(Brushes.Orange, 2);
        }
    }

    private bool IsPathStale(IReadOnlyList<CAP_Core.Routing.PathSegment> segments, WaveguideConnectionViewModel conn)
    {
        if (segments.Count == 0) return false;

        var firstSeg = segments[0];
        var lastSeg = segments[^1];
        double startDist = Math.Sqrt(Math.Pow(firstSeg.StartPoint.X - conn.StartX, 2) +
                                     Math.Pow(firstSeg.StartPoint.Y - conn.StartY, 2));
        double endDist = Math.Sqrt(Math.Pow(lastSeg.EndPoint.X - conn.EndX, 2) +
                                   Math.Pow(lastSeg.EndPoint.Y - conn.EndY, 2));
        return startDist > 1.0 || endDist > 1.0;
    }

    private void DrawPathSegments(DrawingContext context, Pen pen, IReadOnlyList<CAP_Core.Routing.PathSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (segment is StraightSegment straight)
            {
                context.DrawLine(pen,
                    new Point(straight.StartPoint.X, straight.StartPoint.Y),
                    new Point(straight.EndPoint.X, straight.EndPoint.Y));
            }
            else if (segment is BendSegment bend)
            {
                DrawArc(context, pen, bend);
            }
        }
    }

    private void DrawConnectionLabel(DrawingContext context, WaveguideConnectionViewModel conn, DesignCanvasViewModel vm)
    {
        var midX = (conn.StartX + conn.EndX) / 2;
        var midY = (conn.StartY + conn.EndY) / 2;
        string labelText;
        IBrush labelBrush;

        if (vm.ShowPowerFlow && vm.PowerFlowVisualizer.CurrentResult != null)
        {
            var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
            if (flow != null && flow.AveragePower > 0)
            {
                labelText = $"{flow.NormalizedPowerDb:F1}dB ({flow.NormalizedPowerFraction * 100:F0}%)";
                var fraction = Math.Clamp(flow.NormalizedPowerFraction, 0, 1);
                labelBrush = new SolidColorBrush(PowerFlowRenderer.InterpolatePowerColor(fraction));
            }
            else
            {
                labelText = "no signal";
                labelBrush = new SolidColorBrush(Color.FromArgb(120, 150, 150, 150));
            }
        }
        else
        {
            labelText = $"{conn.PathLength:F0}µm, {conn.LossDb:F2}dB";
            labelBrush = Brushes.LightGray;
        }

        var infoText = new FormattedText(
            labelText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            labelBrush);

        context.DrawText(infoText, new Point(midX, midY - 15));
    }

    private void DrawPowerHoverLabel(
        DrawingContext context,
        WaveguideConnectionViewModel conn,
        DesignCanvasViewModel vm)
    {
        var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
        if (flow == null) return;

        var midX = (conn.StartX + conn.EndX) / 2;
        var midY = (conn.StartY + conn.EndY) / 2;
        PowerFlowRenderer.DrawPowerLabel(context, flow, new Point(midX, midY - 25));
    }

    private void DrawArc(DrawingContext context, Pen pen, BendSegment bend)
    {
        int numSegments = Math.Max(8, (int)(Math.Abs(bend.SweepAngleDegrees) / 5));

        if (numSegments == 0 || Math.Abs(bend.SweepAngleDegrees) < 0.1)
        {
            context.DrawLine(pen,
                new Point(bend.StartPoint.X, bend.StartPoint.Y),
                new Point(bend.EndPoint.X, bend.EndPoint.Y));
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
}
