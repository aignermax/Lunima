using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.GroupHierarchyRouting;

/// <summary>
/// Hierarchical routing plan for dense grouped layouts (issue #1175). Partitions the
/// connections of a design by the lowest common ancestor group of their two endpoint
/// components and orders the resulting buckets deepest-first: wires inside a gate group
/// are routed before the wires that stitch gates into a full adder, which are routed
/// before the wires between full adders. Each routed level becomes fixed obstacles for
/// the next, turning one design-wide ordering search into many small local ones.
/// </summary>
public sealed class GroupHierarchyRoutePlan
{
    private GroupHierarchyRoutePlan(List<GroupRoutingBucket> buckets)
    {
        Buckets = buckets;
    }

    /// <summary>Routing buckets ordered deepest group first, then by first appearance.</summary>
    public IReadOnlyList<GroupRoutingBucket> Buckets { get; }

    /// <summary>
    /// True when the plan spans more than one scope — only then does hierarchical
    /// routing differ from the flat pass over the whole design.
    /// </summary>
    public bool HasHierarchy => Buckets.Count > 1;

    /// <summary>
    /// Builds the plan for the given connection snapshot. Connections between
    /// components that share no group fall into the top-level bucket (depth 0).
    /// </summary>
    public static GroupHierarchyRoutePlan Build(IReadOnlyList<WaveguideConnection> connections)
    {
        var buckets = new List<GroupRoutingBucket>();
        var bucketByGroup = new Dictionary<ComponentGroup, GroupRoutingBucket>();
        GroupRoutingBucket? topLevelBucket = null;

        foreach (var connection in connections)
        {
            var (group, depth) = LowestCommonGroup(connection);
            var bucket = group == null
                ? topLevelBucket ??= AddBucket(buckets, null, 0)
                : bucketByGroup.TryGetValue(group, out var existing)
                    ? existing
                    : bucketByGroup[group] = AddBucket(buckets, group, depth);
            bucket.Connections.Add(connection);
        }

        return new GroupHierarchyRoutePlan(
            buckets.OrderByDescending(b => b.Depth)
                .ThenBy(buckets.IndexOf)
                .ToList());
    }

    private static GroupRoutingBucket AddBucket(
        List<GroupRoutingBucket> buckets, ComponentGroup? group, int depth)
    {
        var bucket = new GroupRoutingBucket(group, depth);
        buckets.Add(bucket);
        return bucket;
    }

    /// <summary>
    /// Lowest common ancestor group of a connection's two endpoint components and its
    /// nesting depth (1 = a top-level group). Null/0 when the endpoints share no group.
    /// </summary>
    private static (ComponentGroup? Group, int Depth) LowestCommonGroup(WaveguideConnection connection)
    {
        var startChain = AncestryRootFirst(connection.StartPin.ParentComponent);
        var endChain = AncestryRootFirst(connection.EndPin.ParentComponent);

        int shared = 0;
        while (shared < startChain.Count && shared < endChain.Count &&
               ReferenceEquals(startChain[shared], endChain[shared]))
        {
            shared++;
        }

        return shared == 0 ? (null, 0) : (startChain[shared - 1], shared);
    }

    /// <summary>Groups containing the component, outermost first (root group at index 0).</summary>
    private static List<ComponentGroup> AncestryRootFirst(Component component)
    {
        var chain = new List<ComponentGroup>();
        var current = component.ParentGroup as ComponentGroup;
        while (current != null)
        {
            chain.Add(current);
            current = current.ParentGroup;
        }
        chain.Reverse();
        return chain;
    }
}
