using CAP_Core.Components.Core;

namespace CAP_Core.Components.Connections;

/// <summary>
/// The pins a component brings onto the canvas: its own and, for a group, those of every
/// component nested in it. Wires to a group end at the leaf pins inside it (directly or
/// through an external pin), so deleting a group has to look through it to find its wires.
/// </summary>
public sealed class ComponentWireScope
{
    private readonly HashSet<Component> _owners;

    /// <summary>Creates the scope of <paramref name="component"/> and everything nested in it.</summary>
    public ComponentWireScope(Component component)
    {
        _owners = new HashSet<Component> { component };
        if (component is ComponentGroup group)
            _owners.UnionWith(group.GetAllComponentsRecursive());
    }

    /// <summary>True when <paramref name="pin"/> belongs to the component or one nested in it.</summary>
    public bool Owns(PhysicalPin? pin) => pin?.ParentComponent is { } parent && _owners.Contains(parent);

    /// <summary>True when either end of <paramref name="connection"/> lies inside the scope.</summary>
    public bool Touches(WaveguideConnection connection) =>
        Owns(connection.StartPin) || Owns(connection.EndPin);

    /// <summary>True when <paramref name="component"/> is the scope's component or nested in it.</summary>
    public bool Contains(Component component) => _owners.Contains(component);
}
