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
    /// (non-obstacle components such as die frames or logos) are left out.
    /// </summary>
    public static void Append(
        Component component, double deltaX, double deltaY,
        List<CollisionPrimitive> into, IReadOnlySet<Component>? skip = null)
    {
        if (skip?.Contains(component) == true)
            return;
        if (component is not ComponentGroup group)
        {
            if (IsSolid(component))
                into.Add(CollisionPrimitive.Footprint(component, deltaX, deltaY));
            return;
        }

        foreach (var child in group.ChildComponents)
            Append(child, deltaX, deltaY, into, skip);
        foreach (var path in group.InternalPaths)
            AppendPath(path, group, deltaX, deltaY, into);
    }

    /// <summary>
    /// Cheap outer bounds of everything <see cref="Append"/> would emit (unshifted): leaf
    /// rectangles and tight segment bounds padded by the waveguide half-width. Used to
    /// reject far-away groups before their waveguides are sampled.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) Bounds(Component component)
    {
        var bounds = (MinX: double.MaxValue, MinY: double.MaxValue, MaxX: double.MinValue, MaxY: double.MinValue);
        AccumulateBounds(component, ref bounds);
        return bounds;
    }

    private static void AccumulateBounds(
        Component component, ref (double MinX, double MinY, double MaxX, double MaxY) bounds)
    {
        if (component is not ComponentGroup group)
        {
            if (IsSolid(component))
                Union(ref bounds, component.PhysicalX, component.PhysicalY,
                    component.PhysicalX + component.WidthMicrometers, component.PhysicalY + component.HeightMicrometers);
            return;
        }

        foreach (var child in group.ChildComponents)
            AccumulateBounds(child, ref bounds);
        foreach (var path in group.InternalPaths)
        {
            if (!IsRealGeometry(path))
                continue;
            double pad = HalfWidth(path);
            foreach (var segment in path.Path.Segments)
            {
                var (minX, minY, maxX, maxY) = PathSegmentBounds.Of(segment, pad);
                Union(ref bounds, minX, minY, maxX, maxY);
            }
        }
    }

    private static void AppendPath(
        FrozenWaveguidePath path, ComponentGroup owner, double deltaX, double deltaY, List<CollisionPrimitive> into)
    {
        if (!IsRealGeometry(path))
            return;
        var points = PathIntersectionDetector.SamplePolyline(path.Path);
        double halfWidth = HalfWidth(path);
        var owners = Owners(path, owner);
        if (points.Count == 1)
        {
            var p = points[0];
            into.Add(CollisionPrimitive.Waveguide(
                p.X + deltaX, p.Y + deltaY, p.X + deltaX, p.Y + deltaY, halfWidth, owners));
            return;
        }
        for (int i = 0; i < points.Count - 1; i++)
        {
            into.Add(CollisionPrimitive.Waveguide(
                points[i].X + deltaX, points[i].Y + deltaY,
                points[i + 1].X + deltaX, points[i + 1].Y + deltaY,
                halfWidth, owners));
        }
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

    /// <summary>The waveguide's terminal components, or its owning group when it has no pins.</summary>
    private static IReadOnlyList<Component> Owners(FrozenWaveguidePath path, ComponentGroup owner)
    {
        var owners = new List<Component>(2);
        if (path.StartPin?.ParentComponent is { } start)
            owners.Add(start);
        if (path.EndPin?.ParentComponent is { } end && !ReferenceEquals(end, path.StartPin?.ParentComponent))
            owners.Add(end);
        if (owners.Count == 0)
            owners.Add(owner);
        return owners;
    }

    private static void Union(
        ref (double MinX, double MinY, double MaxX, double MaxY) bounds,
        double minX, double minY, double maxX, double maxY)
    {
        bounds = (Math.Min(bounds.MinX, minX), Math.Min(bounds.MinY, minY),
                  Math.Max(bounds.MaxX, maxX), Math.Max(bounds.MaxY, maxY));
    }
}
