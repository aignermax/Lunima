using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace UnitTests.Simulation;

/// <summary>
/// Holds a test component along with its physical and logical pins for easy access.
/// </summary>
public class ComponentInfo
{
    /// <summary>
    /// The component instance.
    /// </summary>
    public Component Component { get; }

    /// <summary>
    /// Physical pins indexed by name (e.g., "waveguide", "in1", "out1").
    /// </summary>
    public Dictionary<string, PhysicalPin> Pins { get; }

    /// <summary>
    /// All logical pins in the component.
    /// </summary>
    public List<Pin> LogicalPins { get; }

    /// <summary>
    /// Creates a new component info instance.
    /// </summary>
    public ComponentInfo(
        Component component,
        Dictionary<string, PhysicalPin> pins,
        List<Pin> logicalPins)
    {
        Component = component;
        Pins = pins;
        LogicalPins = logicalPins;
    }
}
