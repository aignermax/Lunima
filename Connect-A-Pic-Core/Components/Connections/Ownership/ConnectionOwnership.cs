using System.Collections.Generic;
using System.Linq;
using CAP_Core.Analysis.Hierarchy;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP_Core.Components.Connections.Ownership;

/// <summary>
/// Which group a connection belongs to: the lowest common ancestor group of the two parts
/// it joins. A wire between two parts inside group M — even one drawn while looking at the
/// level above M — is M's wire: it is stored with M, moves with M and is edited inside M.
/// Pins on a group's boundary (exposed group pins) are followed down to the part they stand
/// for, so the owner never depends on which level the wire was drawn from. Null means the
/// design root.
/// </summary>
public static class ConnectionOwnership
{
    /// <summary>The group owning a wire between <paramref name="a"/> and <paramref name="b"/>, or null for the root.</summary>
    public static ComponentGroup? OwnerOf(PhysicalPin a, PhysicalPin b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var ancestorsOfB = new HashSet<ComponentGroup>(AncestorsOf(DesignHierarchy.ResolveToLeafPin(b)));
        return AncestorsOf(DesignHierarchy.ResolveToLeafPin(a)).FirstOrDefault(ancestorsOfB.Contains);
    }

    /// <summary>The group owning <paramref name="connection"/>, or null for the root.</summary>
    public static ComponentGroup? OwnerOf(WaveguideConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return OwnerOf(connection.StartPin, connection.EndPin);
    }

    /// <summary>The die the part behind <paramref name="pin"/> sits on, or null when it is on no die.</summary>
    public static ComponentGroup? DieOf(PhysicalPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        return DieAssembly.DieOf(DesignHierarchy.ResolveToLeafPin(pin).ParentComponent);
    }

    /// <summary>
    /// True when <paramref name="owner"/> lies strictly inside <paramref name="scope"/> (null =
    /// the root): the wire belongs to a group below the level it was drawn on.
    /// </summary>
    public static bool IsBelow(ComponentGroup owner, ComponentGroup? scope)
    {
        ArgumentNullException.ThrowIfNull(owner);
        for (var current = owner.ParentGroup; current != null; current = current.ParentGroup)
        {
            if (ReferenceEquals(current, scope))
                return true;
        }
        return scope == null;
    }

    /// <summary>
    /// The pin a frozen path stored in <paramref name="owner"/> must reference for
    /// <paramref name="pin"/>: boundary pins of <paramref name="owner"/> or of groups around it
    /// are followed inward until the pin belongs to a part (or nested group) inside the owner.
    /// </summary>
    public static PhysicalPin PinInside(PhysicalPin pin, ComponentGroup owner)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(owner);
        var current = pin;
        for (int depth = 0; depth < MaxResolutionDepth && !IsStrictlyInside(current.ParentComponent, owner); depth++)
        {
            if (current.ParentComponent is not ComponentGroup group)
                break;
            var inner = group.ExternalPins.FirstOrDefault(p => p.Name == current.Name)?.InternalPin;
            if (inner == null || ReferenceEquals(inner, current))
                break;
            current = inner;
        }
        return current;
    }

    /// <summary>True when <paramref name="component"/> is <paramref name="group"/> or nested in it.</summary>
    public static bool IsWithin(Component component, ComponentGroup group)
    {
        for (Component? current = component; current != null; current = current.ParentGroup as ComponentGroup)
        {
            if (ReferenceEquals(current, group))
                return true;
        }
        return false;
    }

    private const int MaxResolutionDepth = 64;

    private static bool IsStrictlyInside(Component component, ComponentGroup owner) =>
        !ReferenceEquals(component, owner) && IsWithin(component, owner);

    /// <summary>Groups enclosing the part behind a leaf pin, innermost first.</summary>
    private static IEnumerable<ComponentGroup> AncestorsOf(PhysicalPin leafPin)
    {
        for (var group = leafPin.ParentComponent.ParentGroup as ComponentGroup; group != null; group = group.ParentGroup)
            yield return group;
    }
}
