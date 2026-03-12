using CAP_Core.Components.Core;

namespace CAP_Core.Components.ComponentHelpers;

/// <summary>
/// Represents an instantiated component group on the canvas.
/// Tracks all components that belong to this group instance.
/// </summary>
public class ComponentGroupInstance
{
    /// <summary>
    /// Unique ID for this group instance.
    /// </summary>
    public Guid InstanceId { get; set; }

    /// <summary>
    /// Reference to the group definition this instance was created from.
    /// </summary>
    public ComponentGroup GroupDefinition { get; set; }

    /// <summary>
    /// All components that belong to this group instance.
    /// </summary>
    public List<Component> Components { get; set; } = new();

    /// <summary>
    /// Display name for this group instance (defaults to group definition name).
    /// </summary>
    public string Name => GroupDefinition?.Name ?? "Unnamed Group";

    public ComponentGroupInstance(ComponentGroup groupDefinition)
    {
        InstanceId = Guid.NewGuid();
        GroupDefinition = groupDefinition;
    }
}
