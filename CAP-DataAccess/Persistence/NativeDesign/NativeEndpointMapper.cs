using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Maps stable native connection endpoints through nested component groups.</summary>
public static class NativeEndpointMapper
{
    /// <summary>
    /// Resolves which canvas component, pin name and stable identifiers to use when
    /// serializing a connection endpoint. Handles regular components (direct match),
    /// group external pins (via InternalPin lookup) and — at any nesting depth — pins of
    /// components inside a group that are not exposed as an external pin: those persist
    /// the owning child's identifier together with the identifier of the top-level
    /// ancestor group, so the load path can resolve the endpoint inside that reconstructed
    /// group instead of silently dropping the wire. The ancestor id is required because
    /// child identifiers are only unique within one group, not across groups (two
    /// instances of the same gate template share child identifiers), and because the
    /// component index is not stable across save/load when a design mixes standalone
    /// components and groups. A null component identifier means the endpoint could not
    /// be resolved at all.
    /// </summary>
    /// <param name="components">All top-level components on the canvas.</param>
    /// <param name="pin">The physical pin on the connection endpoint.</param>
    /// <returns>
    /// The component index, pin name, owning component identifier and — only for nested
    /// endpoints — the top-level ancestor group identifier to store in ConnectionData.
    /// </returns>
    public static (int index, string pinName, string? componentId, string? ancestorGroupId)
        ResolveConnectionEndpointWithOwner(List<Component> components, PhysicalPin pin)
    {
        // Direct match: pin belongs to a top-level canvas component
        int directIndex = components.FindIndex(c => c == pin.ParentComponent);
        if (directIndex >= 0)
            return (directIndex, pin.Name, components[directIndex].Identifier, null);

        // Group match: pin is the InternalPin of a group's external pin
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is ComponentGroup group)
            {
                var match = group.ExternalPins.FirstOrDefault(ep => ep.InternalPin == pin);
                if (match != null)
                    return (i, match.Name, group.Identifier, null);
            }
        }

        // Nested match: pin sits on a non-exposed pin of a component inside a group
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is ComponentGroup group)
            {
                var owner = FindPinOwnerRecursive(group, pin);
                if (owner != null)
                    return (i, pin.Name, owner.Identifier, group.Identifier);
            }
        }

        return (-1, pin.Name, null, null);
    }

    /// <summary>
    /// Recursively finds the child component (at any nesting depth) that owns the given pin.
    /// </summary>
    private static Component? FindPinOwnerRecursive(ComponentGroup group, PhysicalPin pin)
    {
        foreach (var child in group.ChildComponents)
        {
            if (child == pin.ParentComponent || child.PhysicalPins.Contains(pin))
                return child;
            if (child is ComponentGroup nested)
            {
                var found = FindPinOwnerRecursive(nested, pin);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the physical pin to connect to on a component during load.
    /// Handles both regular components (PhysicalPins lookup) and groups (ExternalPins lookup via external pin name).
    /// </summary>
    /// <param name="component">The component to find the pin on.</param>
    /// <param name="pinName">The pin name stored in ConnectionData.</param>
    /// <returns>The physical pin, or null if not found.</returns>
    public static PhysicalPin? ResolvePin(Component component, string pinName)
    {
        // For regular components: look up by physical pin name directly
        var directPin = component.PhysicalPins.FirstOrDefault(p => p.Name == pinName);
        if (directPin != null)
            return directPin;

        // For groups: look up by external pin name and return its InternalPin
        if (component is ComponentGroup group)
            return group.ExternalPins.FirstOrDefault(ep => ep.Name == pinName)?.InternalPin;

        return null;
    }

    /// <summary>
    /// Recursively finds the child component with the given identifier inside a group.
    /// </summary>
    public static Component? FindComponentByIdentifierRecursive(ComponentGroup group, string identifier)
    {
        foreach (var child in group.ChildComponents)
        {
            if (child.Identifier == identifier)
                return child;
            if (child is ComponentGroup nested)
            {
                var found = FindComponentByIdentifierRecursive(nested, identifier);
                if (found != null)
                    return found;
            }
        }
        return null;
    }
}
