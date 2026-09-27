using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.GroupHierarchyRouting;

/// <summary>
/// One routing unit of a <see cref="GroupHierarchyRoutePlan"/>: all connections whose
/// lowest common ancestor group is <see cref="Group"/>. A null group is the design's
/// top level (wires between top-level components or between top-level groups).
/// </summary>
public sealed class GroupRoutingBucket
{
    /// <summary>
    /// Creates a bucket for one hierarchy scope.
    /// </summary>
    /// <param name="group">Lowest common ancestor group of the bucket's wires; null = top level.</param>
    /// <param name="depth">Nesting depth of <paramref name="group"/> (0 = top level).</param>
    public GroupRoutingBucket(ComponentGroup? group, int depth)
    {
        Group = group;
        Depth = depth;
    }

    /// <summary>Lowest common ancestor group of every wire in this bucket; null = top level.</summary>
    public ComponentGroup? Group { get; }

    /// <summary>Nesting depth of <see cref="Group"/>: 0 for the top level, 1 for a top-level group, …</summary>
    public int Depth { get; }

    /// <summary>Connections belonging to this scope, in snapshot order.</summary>
    public List<WaveguideConnection> Connections { get; } = new();
}
