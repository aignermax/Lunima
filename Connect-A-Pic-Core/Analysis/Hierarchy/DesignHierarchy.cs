using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis.Hierarchy;

/// <summary>
/// One frozen waveguide path found anywhere in a group hierarchy, together with the
/// group that owns it and that group's name path ("OUTER/INNER").
/// </summary>
/// <param name="Frozen">The frozen path (stored in absolute coordinates).</param>
/// <param name="Owner">The group whose <see cref="ComponentGroup.InternalPaths"/> hold the path.</param>
/// <param name="GroupPath">Name path of <paramref name="Owner"/> itself, outermost first (see <see cref="DesignHierarchy.PathOf"/>).</param>
public sealed record HierarchicalFrozenPath(
    FrozenWaveguidePath Frozen,
    ComponentGroup Owner,
    string GroupPath);

/// <summary>
/// Flattened view of a design's group hierarchy for the design checks: every leaf
/// component and every frozen internal path at any nesting depth. Group children and
/// frozen paths carry absolute coordinates, so the flattened lists can be checked with
/// the same geometry code as top-level items. Built once per validation pass and reused
/// by every hierarchy-aware check, so nested groups are covered uniformly.
/// </summary>
public sealed class DesignHierarchy
{
    private const int MaxPinResolutionDepth = 64;

    private readonly List<Component> _leaves = new();
    private readonly List<ComponentGroup> _groups = new();
    private readonly List<HierarchicalFrozenPath> _frozenPaths = new();
    private readonly HashSet<Component> _visited = new(ReferenceEqualityComparer.Instance);

    /// <summary>Non-group components at any depth, top-level ones included.</summary>
    public IReadOnlyList<Component> Leaves => _leaves;

    /// <summary>Every group at any depth, outer groups before their nested groups.</summary>
    public IReadOnlyList<ComponentGroup> Groups => _groups;

    /// <summary>Every frozen internal path of every group at any depth.</summary>
    public IReadOnlyList<HierarchicalFrozenPath> FrozenPaths => _frozenPaths;

    private DesignHierarchy()
    {
    }

    /// <summary>
    /// Flattens the given placed items. Groups are descended recursively; an item that is
    /// reachable more than once (e.g. a child passed alongside its group) is visited once.
    /// </summary>
    /// <param name="items">Placed items — usually the top-level canvas components and groups.</param>
    public static DesignHierarchy From(IEnumerable<Component> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var hierarchy = new DesignHierarchy();
        foreach (var item in items)
            hierarchy.Visit(item);
        return hierarchy;
    }

    /// <summary>
    /// Group-name path of the groups enclosing <paramref name="component"/>, outermost
    /// first ("ALU/AND0"), or null for a top-level component.
    /// </summary>
    public static string? GroupNamePathOf(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        return component.ParentGroup is ComponentGroup parent ? PathOf(parent) : null;
    }

    /// <summary>
    /// Name path of <paramref name="group"/> itself, outermost first ("RAM/CELL1"); a
    /// group without a name contributes its identifier.
    /// </summary>
    public static string PathOf(ComponentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var names = new List<string>();
        for (var current = group; current != null; current = current.ParentGroup)
            names.Add(string.IsNullOrWhiteSpace(current.GroupName) ? current.Identifier : current.GroupName);
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>
    /// Prefixes <paramref name="name"/> with the enclosing group-name path of
    /// <paramref name="component"/> ("ALU/AND0/mmi_1"); unchanged for top-level components.
    /// </summary>
    public static string QualifyName(Component component, string name)
    {
        var groupPath = GroupNamePathOf(component);
        return groupPath is null ? name : $"{groupPath}/{name}";
    }

    /// <summary>
    /// Follows a group's exposed pin down to the leaf component pin it stands for. A
    /// group's <see cref="Component.PhysicalPins"/> are synthesized copies of its
    /// <see cref="ComponentGroup.ExternalPins"/> (see
    /// <see cref="ComponentGroup.SyncPhysicalPinsFromExternalPins"/>), and an outer group
    /// may expose a nested group's synthesized pin, so the chain is followed until a
    /// non-group pin is reached. Leaf pins are returned unchanged.
    /// </summary>
    public static PhysicalPin ResolveToLeafPin(PhysicalPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        var current = pin;
        for (int depth = 0; depth < MaxPinResolutionDepth; depth++)
        {
            if (current.ParentComponent is not ComponentGroup group)
                return current;

            var exposed = group.ExternalPins.FirstOrDefault(e => e.Name == current.Name)?.InternalPin;
            if (exposed is null || ReferenceEquals(exposed, current))
                return current;
            current = exposed;
        }
        return current;
    }

    /// <summary>
    /// Axis-aligned bounds of a group's real geometry — every leaf footprint and every
    /// frozen path segment at any depth — or null for an empty group. Unlike the cached
    /// <see cref="Component.WidthMicrometers"/>, this never depends on where the group's
    /// own origin sits relative to its children.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? GeometryBoundsOf(ComponentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var hierarchy = From(new Component[] { group });
        var boxes = hierarchy.Leaves
            .Select(l => (l.PhysicalX, l.PhysicalY, l.PhysicalX + l.WidthMicrometers, l.PhysicalY + l.HeightMicrometers))
            .Concat(hierarchy.FrozenPaths
                .SelectMany(p => p.Frozen.Path?.Segments ?? Enumerable.Empty<PathSegment>())
                .Select(s => PathSegmentBounds.Of(s)))
            .ToList();

        if (boxes.Count == 0)
            return null;
        return (boxes.Min(b => b.Item1), boxes.Min(b => b.Item2), boxes.Max(b => b.Item3), boxes.Max(b => b.Item4));
    }

    private void Visit(Component item)
    {
        if (!_visited.Add(item))
            return;

        if (item is not ComponentGroup group)
        {
            _leaves.Add(item);
            return;
        }

        _groups.Add(group);
        var groupPath = PathOf(group);
        foreach (var frozen in group.InternalPaths)
            _frozenPaths.Add(new HierarchicalFrozenPath(frozen, group, groupPath));
        foreach (var child in group.ChildComponents)
            Visit(child);
    }

}
