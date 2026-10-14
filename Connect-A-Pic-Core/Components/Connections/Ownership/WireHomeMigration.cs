using System.Collections.Generic;
using System.Linq;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Connections.Ownership;

/// <summary>A root wire that belongs to a group (its pins' lowest common ancestor).</summary>
/// <param name="Connection">The live root connection.</param>
/// <param name="Owner">The group it must be stored in.</param>
public sealed record RootWireToGroup(WaveguideConnection Connection, ComponentGroup Owner);

/// <summary>A frozen wire stored too high up: it moves down into the group that owns it.</summary>
/// <param name="From">The group currently holding the path.</param>
/// <param name="Path">The frozen path.</param>
/// <param name="To">The owning group nested in <paramref name="From"/>.</param>
public sealed record GroupWireMove(ComponentGroup From, FrozenWaveguidePath Path, ComponentGroup To);

/// <summary>A wire stored inside a die: a die is a work surface, its wires live on the assembly canvas.</summary>
/// <param name="Die">The die holding the path.</param>
/// <param name="Path">The frozen path to turn into a live assembly connection.</param>
public sealed record DieWireToSurface(ComponentGroup Die, FrozenWaveguidePath Path);

/// <summary>Everything that has to move so every wire sits with its owner.</summary>
/// <param name="RootWiresToGroups">Root connections to freeze into their owning group.</param>
/// <param name="GroupWiresMovedDown">Frozen paths to move into a nested owning group.</param>
/// <param name="DieWiresToSurface">Frozen die paths to bring onto the assembly canvas.</param>
public sealed record WireHomePlan(
    IReadOnlyList<RootWireToGroup> RootWiresToGroups,
    IReadOnlyList<GroupWireMove> GroupWiresMovedDown,
    IReadOnlyList<DieWireToSurface> DieWiresToSurface)
{
    /// <summary>True when every wire already sits with its owner.</summary>
    public bool IsEmpty => RootWiresToGroups.Count == 0 && GroupWiresMovedDown.Count == 0 && DieWiresToSurface.Count == 0;
}

/// <summary>
/// Load-time migration to the ownership rule (see <see cref="ConnectionOwnership"/>): a wire
/// drawn on a level above its parts' common group is stored with that group, a frozen path
/// stored too high moves down, and wires stored inside a die come back onto the assembly
/// canvas (a die is edited in place, never entered). Idempotent — a design whose wires all
/// sit with their owner produces an empty plan, so single-die and plain designs that never
/// had misplaced wires are left untouched.
/// </summary>
public static class WireHomeMigration
{
    /// <summary>Plans the moves for the design formed by <paramref name="topLevel"/> and its root wires.</summary>
    public static WireHomePlan Plan(IEnumerable<Component> topLevel, IEnumerable<WaveguideConnection> rootConnections)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(rootConnections);

        var rootMoves = new List<RootWireToGroup>();
        foreach (var connection in rootConnections)
        {
            if (connection.IsCrossChipletFacetLink)
                continue;
            if (ConnectionOwnership.OwnerOf(connection) is { IsDie: false } owner)
                rootMoves.Add(new RootWireToGroup(connection, owner));
        }

        var downMoves = new List<GroupWireMove>();
        var surfaced = new List<DieWireToSurface>();
        foreach (var group in topLevel.OfType<ComponentGroup>())
            Visit(group, downMoves, surfaced);
        return new WireHomePlan(rootMoves, downMoves, surfaced);
    }

    /// <summary>
    /// Applies the group-to-group moves of <paramref name="plan"/> and removes the surfaced
    /// paths from their dies. Root wires are left to the caller, which owns the live canvas.
    /// </summary>
    public static void ApplyModelMoves(WireHomePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var move in plan.GroupWiresMovedDown)
        {
            move.From.RemoveInternalPath(move.Path);
            move.Path.StartPin = ConnectionOwnership.PinInside(move.Path.StartPin!, move.To);
            move.Path.EndPin = ConnectionOwnership.PinInside(move.Path.EndPin!, move.To);
            GroupWireHoming.Home(move.To, move.Path);
        }
        foreach (var wire in plan.DieWiresToSurface)
            wire.Die.RemoveInternalPath(wire.Path);
    }

    private static void Visit(ComponentGroup group, List<GroupWireMove> downMoves, List<DieWireToSurface> surfaced)
    {
        foreach (var path in group.InternalPaths)
        {
            if (path.StartPin is null || path.EndPin is null)
                continue;
            var owner = ConnectionOwnership.OwnerOf(path.StartPin, path.EndPin);
            if (owner == null || ReferenceEquals(owner, group))
            {
                if (group.IsDie)
                    surfaced.Add(new DieWireToSurface(group, path));
                continue;
            }
            if (!ConnectionOwnership.IsWithin(owner, group))
                continue;
            if (owner.IsDie)
                surfaced.Add(new DieWireToSurface(owner, path));
            else
                downMoves.Add(new GroupWireMove(group, path, owner));
        }
        foreach (var child in group.ChildComponents.OfType<ComponentGroup>())
            Visit(child, downMoves, surfaced);
    }
}
