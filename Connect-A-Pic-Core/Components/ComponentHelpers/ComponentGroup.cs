using CAP_Core.Components.Core;

namespace CAP_Core.Components.ComponentHelpers;

/// <summary>
/// Represents a reusable group of components with their relative positions and connections.
/// Used for creating library templates of common circuit patterns (e.g., MZI, ring resonator networks).
/// </summary>
public class ComponentGroup
{
    /// <summary>
    /// Unique identifier for this component group.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Display name for this group (e.g., "Mach-Zehnder Interferometer").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Category for organizing groups in the library (e.g., "Interferometers", "Filters").
    /// </summary>
    public string Category { get; set; } = "User Defined";

    /// <summary>
    /// Optional description of what this group does.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Bounding box width in micrometers (for preview rendering).
    /// </summary>
    public double WidthMicrometers { get; set; }

    /// <summary>
    /// Bounding box height in micrometers (for preview rendering).
    /// </summary>
    public double HeightMicrometers { get; set; }

    /// <summary>
    /// Component definitions with relative positions.
    /// Positions are relative to the group's top-left origin.
    /// </summary>
    public List<ComponentGroupMember> Components { get; set; } = new();

    /// <summary>
    /// Connection definitions between components in this group.
    /// </summary>
    public List<GroupConnection> Connections { get; set; } = new();

    /// <summary>
    /// Timestamp when this group was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when this group was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; }
}

/// <summary>
/// Represents a single component within a ComponentGroup.
/// </summary>
public class ComponentGroupMember
{
    /// <summary>
    /// Local ID within this group (0, 1, 2...) for referencing in connections.
    /// </summary>
    public int LocalId { get; set; }

    /// <summary>
    /// Template name from ComponentTemplates (e.g., "1x2 MMI Splitter").
    /// </summary>
    public string TemplateName { get; set; } = "";

    /// <summary>
    /// Relative X position in micrometers from group origin.
    /// </summary>
    public double RelativeX { get; set; }

    /// <summary>
    /// Relative Y position in micrometers from group origin.
    /// </summary>
    public double RelativeY { get; set; }

    /// <summary>
    /// Component rotation (0, 90, 180, 270 degrees).
    /// </summary>
    public DiscreteRotation Rotation { get; set; } = DiscreteRotation.R0;

    /// <summary>
    /// Parameter values for parametric components (e.g., slider values).
    /// </summary>
    public Dictionary<string, double> Parameters { get; set; } = new();
}

/// <summary>
/// Represents a connection between two components in a ComponentGroup.
/// </summary>
public class GroupConnection
{
    /// <summary>
    /// LocalId of the source component.
    /// </summary>
    public int SourceComponentId { get; set; }

    /// <summary>
    /// Pin name on the source component (e.g., "out1").
    /// </summary>
    public string SourcePinName { get; set; } = "";

    /// <summary>
    /// LocalId of the target component.
    /// </summary>
    public int TargetComponentId { get; set; }

    /// <summary>
    /// Pin name on the target component (e.g., "in").
    /// </summary>
    public string TargetPinName { get; set; } = "";
}
