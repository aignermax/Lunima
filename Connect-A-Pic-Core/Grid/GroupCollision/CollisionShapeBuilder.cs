using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP_Core.Grid.GroupCollision;

/// <summary>
/// Turns a placed component — a plain component or a group at any nesting depth — into
/// the real geometry the placement check compares: one footprint rectangle per leaf
/// component and the frozen waveguide centerlines (arcs sampled into straight pieces)
/// of every group level. A group's own bounding box is never emitted.
/// </summary>
internal static class CollisionShapeBuilder
{
    /// <summary>
    /// Appends the primitives of <paramref name="component"/>, shifted by the delta, to
    /// <paramref name="into"/>. Leaves in <paramref name="skip"/> and background geometry
    /// (non-obstacle components such as die frames or logos) are left out. With a
    /// <paramref name="window"/>, only geometry whose (unshifted) bounds touch it is
    /// emitted — far-away waveguides of a large obstacle group are never sampled.
    /// </summary>
    public static void Append(
        Component component, double deltaX, double deltaY,
        List<CollisionPrimitive> into,
        IReadOnlySet<Component>? skip = null,
        CollisionWindow? window = null)
    {
        if (skip?.Contains(component) == true)
            return;
        if (component is not ComponentGroup group)
        {
            if (IsSolid(component) && Touches(window, component))
                into.Add(CollisionPrimitive.Footprint(component, deltaX, deltaY));
            return;
        }

        foreach (var child in group.ChildComponents)
            Append(child, deltaX, deltaY, into, skip, window);
        foreach (var path in group.InternalPaths)
        {
            if (IsRealGeometry(path) && Touches(window, path))
                AppendPath(path, group, deltaX, deltaY, into);
        }
    }

    private static void AppendPath(
        FrozenWaveguidePath path, ComponentGroup owner, double deltaX, double deltaY, List<CollisionPrimitive> into)
    {
        var points = PathIntersectionDetector.SamplePolyline(path.Path);
        double halfWidth = HalfWidth(path);
        if (points.Count == 1)
        {
            var p = points[0];
            into.Add(CollisionPrimitive.Waveguide(
                p.X + deltaX, p.Y + deltaY, p.X + deltaX, p.Y + deltaY, halfWidth, owner));
            return;
        }
        for (int i = 0; i < points.Count - 1; i++)
        {
            into.Add(CollisionPrimitive.Waveguide(
                points[i].X + deltaX, points[i].Y + deltaY,
                points[i + 1].X + deltaX, points[i + 1].Y + deltaY,
                halfWidth, owner));
        }
    }

    private static bool Touches(CollisionWindow? window, Component leaf) =>
        window is not { } w
        || w.Intersects(leaf.PhysicalX, leaf.PhysicalY,
            leaf.PhysicalX + leaf.WidthMicrometers, leaf.PhysicalY + leaf.HeightMicrometers);

    private static bool Touches(CollisionWindow? window, FrozenWaveguidePath path)
    {
        if (window is not { } w)
            return true;
        double pad = HalfWidth(path);
        foreach (var segment in path.Path.Segments)
        {
            var (minX, minY, maxX, maxY) = PathSegmentBounds.Of(segment, pad);
            if (w.Intersects(minX, minY, maxX, maxY))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Blocked fallbacks are straight placeholders, not fabricated geometry (the DRC
    /// reports them separately), so they never block a placement.
    /// </summary>
    private static bool IsRealGeometry(FrozenWaveguidePath path) =>
        path.Path is { Segments.Count: > 0, IsBlockedFallback: false };

    private static bool IsSolid(Component component) =>
        component.IsRoutingObstacle && component.WidthMicrometers > 0 && component.HeightMicrometers > 0;

    private static double HalfWidth(FrozenWaveguidePath path) =>
        Math.Max(path.WidthMicrometers, 0) / 2.0;
}

/// <summary>Axis-aligned region outside of which obstacle geometry cannot matter.</summary>
internal readonly record struct CollisionWindow(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>True when the closed box overlaps this window.</summary>
    public bool Intersects(double minX, double minY, double maxX, double maxY) =>
        minX <= MaxX && maxX >= MinX && minY <= MaxY && maxY >= MinY;
}
