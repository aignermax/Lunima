using CAP_Core.Components.Core;
using CAP_Core.Grid.GroupCollision;

namespace CAP_Core.Grid;

/// <summary>
/// Placement collision check for groups (and for boxes placed next to groups) based on
/// the real placed geometry, never on a group's bounding box: every leaf component's
/// footprint (with the component gap) and every frozen waveguide's centerline (with its
/// half-width plus the minimum waveguide spacing), at any nesting depth, on both the
/// moved side and the obstacle side. Two groups may therefore interleave as long as their
/// parts and waveguides stay clear of each other.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1822:Mark members as static",
    Justification = "Instance API kept stable for its existing callers.")]
public class GroupCollisionDetector
{
    /// <summary>
    /// Checks if a ComponentGroup can be placed at a new position without its parts or
    /// frozen waveguides colliding with the parts or frozen waveguides of anything else.
    /// </summary>
    /// <param name="group">The ComponentGroup being moved.</param>
    /// <param name="newX">New X position for the group.</param>
    /// <param name="newY">New Y position for the group.</param>
    /// <param name="allComponents">All top-level components on the canvas (the moved group may be included).</param>
    /// <param name="excludeFromCollision">Components to exclude from collision checks (e.g. other members of a multi-selection).</param>
    /// <param name="preDragOverlapPartners">
    /// Optional grandfather map for re-dropping a dragged group onto deliberately stacked
    /// placements (e.g. an exact GDS import), as produced by <see cref="FindContacts"/> when
    /// the drag began: keyed by a moved leaf (or the group owning moved frozen waveguides),
    /// the leaves (or waveguide-owning groups) it already touched. Those pairs never
    /// reject; anything else does.
    /// </param>
    /// <returns>True if placement is valid (no collisions), false otherwise.</returns>
    public bool CanPlaceGroup(
        ComponentGroup group,
        double newX,
        double newY,
        IEnumerable<Component> allComponents,
        HashSet<Component>? excludeFromCollision = null,
        IReadOnlyDictionary<Component, HashSet<Component>>? preDragOverlapPartners = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(allComponents);

        var moved = new List<CollisionPrimitive>();
        CollisionShapeBuilder.Append(group, newX - group.PhysicalX, newY - group.PhysicalY, moved);
        var skip = SkipSet(group, excludeFromCollision);
        return !new PlacementCollisionQuery(moved, preDragOverlapPartners).HasCollision(allComponents, skip);
    }

    /// <summary>
    /// Checks if a plain footprint rectangle can be placed without coming within the
    /// component gap of a part, or within waveguide clearance of a frozen waveguide, of
    /// the given obstacles — groups are compared by their parts and waveguides only.
    /// </summary>
    /// <param name="x">Footprint left edge.</param>
    /// <param name="y">Footprint top edge.</param>
    /// <param name="width">Footprint width.</param>
    /// <param name="height">Footprint height.</param>
    /// <param name="obstacles">Obstacles to test against (plain components or groups).</param>
    /// <param name="movedComponent">The component being placed, if any (excluded from obstacles, grandfather key).</param>
    /// <param name="preDragOverlapPartners">Optional grandfathered contacts, see <see cref="CanPlaceGroup"/>.</param>
    /// <returns>True if the footprint stands clear.</returns>
    public bool CanPlaceFootprint(
        double x, double y, double width, double height,
        IEnumerable<Component> obstacles,
        Component? movedComponent = null,
        IReadOnlyDictionary<Component, HashSet<Component>>? preDragOverlapPartners = null)
    {
        ArgumentNullException.ThrowIfNull(obstacles);

        var moved = new List<CollisionPrimitive> { CollisionPrimitive.Footprint(x, y, width, height, movedComponent) };
        var skip = movedComponent is null ? new HashSet<Component>() : new HashSet<Component> { movedComponent };
        return !new PlacementCollisionQuery(moved, preDragOverlapPartners).HasCollision(obstacles, skip);
    }

    /// <summary>
    /// Every contact <paramref name="moved"/> (a plain component or a group) currently has
    /// with the other components under the placement rules — keyed by the moved leaf (or the
    /// group owning moved frozen waveguides), valued with the touched leaves (or the groups
    /// owning the touched waveguides). Captured
    /// when a drag begins and passed back as the grandfather map so pre-existing contacts
    /// (deliberately stacked imports, butt-coupled facets) do not block a re-drop.
    /// </summary>
    /// <param name="moved">The component or group about to be moved.</param>
    /// <param name="allComponents">All top-level components on the canvas.</param>
    /// <param name="excludeFromCollision">Components that are never obstacles.</param>
    /// <returns>The contact map, empty when everything stands clear.</returns>
    public Dictionary<Component, HashSet<Component>> FindContacts(
        Component moved,
        IEnumerable<Component> allComponents,
        HashSet<Component>? excludeFromCollision = null)
    {
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(allComponents);

        var primitives = new List<CollisionPrimitive>();
        CollisionShapeBuilder.Append(moved, 0, 0, primitives);
        var skip = SkipSet(moved, excludeFromCollision);
        return new PlacementCollisionQuery(primitives, null)
            .FindContacts(allComponents, skip);
    }

    private static HashSet<Component> SkipSet(Component moved, HashSet<Component>? excludeFromCollision)
    {
        var skip = new HashSet<Component> { moved };
        if (moved is ComponentGroup group)
            skip.UnionWith(group.GetAllComponentsRecursive());
        if (excludeFromCollision is not null)
            skip.UnionWith(excludeFromCollision);
        return skip;
    }
}
