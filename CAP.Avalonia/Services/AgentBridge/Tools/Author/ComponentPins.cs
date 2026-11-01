using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>A pin as agents address it: the name they use and the physical pin a wire attaches to.</summary>
/// <param name="Name">Pin name (a group's external pin name for groups).</param>
/// <param name="Pin">The physical pin a waveguide connects to.</param>
public sealed record AgentPin(string Name, PhysicalPin Pin);

/// <summary>
/// The connectable pins of a top-level component. A group (or die) is wired through its
/// external pins, whose wires attach to the internal pin each one exposes — the same rule the
/// canvas and the in-app assistant use.
/// </summary>
public static class ComponentPins
{
    /// <summary>The component's connectable pins in declaration order.</summary>
    public static IReadOnlyList<AgentPin> Of(Component component) =>
        component is ComponentGroup { ExternalPins.Count: > 0 } group
            ? group.ExternalPins
                .Where(external => external.InternalPin != null)
                .Select(external => new AgentPin(external.Name, external.InternalPin))
                .ToList()
            : component.PhysicalPins.Select(pin => new AgentPin(pin.Name, pin)).ToList();

    /// <summary>The pin called <paramref name="name"/> (exact, then case-insensitive), or null.</summary>
    public static AgentPin? Find(Component component, string name)
    {
        var pins = Of(component);
        return pins.FirstOrDefault(p => p.Name == name)
            ?? pins.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
