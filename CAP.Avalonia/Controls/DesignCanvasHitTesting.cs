using Avalonia;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Provides hit testing functionality for canvas elements (components, pins, connections).
/// </summary>
public class DesignCanvasHitTesting
{
    private const double PinHitRadius = 15.0;
    private const double ConnectionHitTolerance = 10.0;

    /// <summary>
    /// Finds the component at the given canvas point (topmost first).
    /// For ComponentGroups, checks if the point is within the group's bounding box.
    /// </summary>
    public static ComponentViewModel? HitTestComponent(Point canvasPoint, DesignCanvasViewModel? vm)
    {
        if (vm == null) return null;

        for (int i = vm.Components.Count - 1; i >= 0; i--)
        {
            var comp = vm.Components[i];

            // For ComponentGroups, check the group's calculated bounds
            if (comp.Component is ComponentGroup group)
            {
                var groupRect = CalculateGroupBounds(group);
                if (groupRect.Contains(canvasPoint))
                {
                    return comp;
                }
            }
            else
            {
                var rect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);
                if (rect.Contains(canvasPoint))
                {
                    return comp;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Calculates the bounding rectangle for a ComponentGroup based on its children.
    /// </summary>
    private static Rect CalculateGroupBounds(ComponentGroup group)
    {
        if (group.ChildComponents.Count == 0)
        {
            return new Rect(group.PhysicalX, group.PhysicalY, group.WidthMicrometers, group.HeightMicrometers);
        }

        double minX = group.ChildComponents.Min(c => c.PhysicalX);
        double minY = group.ChildComponents.Min(c => c.PhysicalY);
        double maxX = group.ChildComponents.Max(c => c.PhysicalX + c.WidthMicrometers);
        double maxY = group.ChildComponents.Max(c => c.PhysicalY + c.HeightMicrometers);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Finds the nearest pin within hit radius of the given canvas point.
    /// </summary>
    public static PhysicalPin? HitTestPin(Point canvasPoint, DesignCanvasViewModel? vm)
    {
        if (vm == null) return null;

        PhysicalPin? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (var comp in vm.Components)
        {
            foreach (var pin in comp.Component.PhysicalPins)
            {
                var (pinX, pinY) = pin.GetAbsolutePosition();
                var distance = Math.Sqrt(Math.Pow(canvasPoint.X - pinX, 2) + Math.Pow(canvasPoint.Y - pinY, 2));
                if (distance < nearestDistance && distance <= PinHitRadius)
                {
                    nearest = pin;
                    nearestDistance = distance;
                }
            }
        }

        return nearest;
    }

    /// <summary>
    /// Finds the connection nearest to the given canvas point within tolerance.
    /// </summary>
    public static WaveguideConnectionViewModel? HitTestConnection(Point canvasPoint, DesignCanvasViewModel? vm)
    {
        if (vm == null) return null;

        foreach (var conn in vm.Connections)
        {
            double distance = PointToSegmentDistance(
                canvasPoint.X, canvasPoint.Y,
                conn.StartX, conn.StartY,
                conn.EndX, conn.EndY);

            if (distance <= ConnectionHitTolerance)
                return conn;
        }

        return null;
    }

    /// <summary>
    /// Calculates the minimum distance from a point to a line segment.
    /// </summary>
    private static double PointToSegmentDistance(
        double px, double py,
        double x1, double y1,
        double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSq = dx * dx + dy * dy;

        if (lengthSq < 0.0001)
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));

        var t = Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / lengthSq));
        var projX = x1 + t * dx;
        var projY = y1 + t * dy;

        return Math.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
    }
}
