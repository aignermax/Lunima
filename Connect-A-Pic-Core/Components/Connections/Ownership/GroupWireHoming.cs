using System.Collections.Generic;
using System.Linq;
using CAP_Core.Analysis.Hierarchy;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP_Core.Components.Connections.Ownership;

/// <summary>
/// What <see cref="GroupWireHoming.Home"/> changed, so <see cref="GroupWireHoming.Unhome"/>
/// can put the groups back exactly (including the position of every boundary pin it dropped).
/// </summary>
/// <param name="Owner">The group the wire was stored in.</param>
/// <param name="Path">The frozen path added to <paramref name="Owner"/>.</param>
/// <param name="RemovedBoundaryPins">Boundary pins dropped because the wire now consumes them, with their list index.</param>
/// <param name="RemovedGroupPins">The groups' own (synthesized) pins standing for those boundary pins, with their list index.</param>
public sealed record GroupWireHome(
    ComponentGroup Owner,
    FrozenWaveguidePath Path,
    IReadOnlyList<(ComponentGroup Group, GroupPin Pin, int Index)> RemovedBoundaryPins,
    IReadOnlyList<(ComponentGroup Group, PhysicalPin Pin, int Index)> RemovedGroupPins);

/// <summary>
/// Stores a wire in the group that owns it (see <see cref="ConnectionOwnership"/>) as frozen
/// geometry: the routed path and every routing setting are kept, the pins are re-expressed
/// inside the owner, and the boundary pins of the owner and of the groups around it that
/// exposed the now internally wired pins are dropped — the same interface a group gets when
/// it is created around an already wired pair. Dies are never homed into: a die is a work
/// surface whose wires stay live and editable on the assembly canvas.
/// </summary>
public static class GroupWireHoming
{
    /// <summary>Frozen copy of <paramref name="connection"/> with its pins expressed inside <paramref name="owner"/>.</summary>
    public static FrozenWaveguidePath Freeze(WaveguideConnection connection, ComponentGroup owner)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(owner);
        var frozen = new FrozenWaveguidePath
        {
            Path = connection.RoutedPath?.DeepCopy() ?? new RoutedPath(),
            StartPin = ConnectionOwnership.PinInside(connection.StartPin, owner),
            EndPin = ConnectionOwnership.PinInside(connection.EndPin, owner),
        };
        frozen.CaptureSettingsFrom(connection);
        return frozen;
    }

    /// <summary>Adds <paramref name="path"/> to <paramref name="owner"/> and drops the boundary pins it consumes.</summary>
    public static GroupWireHome Home(ComponentGroup owner, FrozenWaveguidePath path)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(path);
        var consumed = new HashSet<PhysicalPin>();
        if (path.StartPin != null) consumed.Add(DesignHierarchy.ResolveToLeafPin(path.StartPin));
        if (path.EndPin != null) consumed.Add(DesignHierarchy.ResolveToLeafPin(path.EndPin));

        var removed = new List<(ComponentGroup, GroupPin, int)>();
        var removedGroupPins = new List<(ComponentGroup, PhysicalPin, int)>();
        for (var group = owner; group != null && !group.IsDie; group = group.ParentGroup)
        {
            for (int i = group.ExternalPins.Count - 1; i >= 0; i--)
            {
                var pin = group.ExternalPins[i];
                if (!consumed.Contains(DesignHierarchy.ResolveToLeafPin(pin.InternalPin)))
                    continue;
                removed.Add((group, pin, i));
                group.RemoveExternalPin(pin);
                RemoveGroupPin(group, pin.Name, removedGroupPins);
            }
        }
        owner.AddInternalPath(path);
        return new GroupWireHome(owner, path, removed, removedGroupPins);
    }

    /// <summary>
    /// Drops the group's own pin named like a dropped boundary pin. The remaining pin objects
    /// are kept (wires on the level above may hold them), so no full re-sync happens here.
    /// </summary>
    private static void RemoveGroupPin(ComponentGroup group, string name, List<(ComponentGroup, PhysicalPin, int)> removed)
    {
        int index = group.PhysicalPins.FindIndex(p => p.Name == name);
        if (index < 0)
            return;
        removed.Add((group, group.PhysicalPins[index], index));
        group.PhysicalPins.RemoveAt(index);
    }

    /// <summary>Reverses <see cref="Home"/>: removes the path and restores the dropped boundary pins in place.</summary>
    public static void Unhome(GroupWireHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        home.Owner.RemoveInternalPath(home.Path);
        foreach (var (group, pin, index) in home.RemovedBoundaryPins.Reverse())
            group.InsertExternalPin(index, pin);
        foreach (var (group, pin, index) in home.RemovedGroupPins.Reverse())
            group.PhysicalPins.Insert(Math.Min(index, group.PhysicalPins.Count), pin);
    }
}
