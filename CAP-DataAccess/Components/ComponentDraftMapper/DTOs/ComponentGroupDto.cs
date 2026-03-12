namespace CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

/// <summary>
/// Data transfer object for serializing ComponentGroup to JSON.
/// </summary>
public class ComponentGroupDto
{
    /// <summary>
    /// Unique identifier for this component group.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Display name for this group.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Category for organizing groups in the library.
    /// </summary>
    public string Category { get; set; } = "User Defined";

    /// <summary>
    /// Optional description of what this group does.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Bounding box width in micrometers.
    /// </summary>
    public double WidthMicrometers { get; set; }

    /// <summary>
    /// Bounding box height in micrometers.
    /// </summary>
    public double HeightMicrometers { get; set; }

    /// <summary>
    /// Component members with relative positions.
    /// </summary>
    public List<ComponentGroupMemberDto> Components { get; set; } = new();

    /// <summary>
    /// Connection definitions between components.
    /// </summary>
    public List<GroupConnectionDto> Connections { get; set; } = new();

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
/// DTO for a component within a ComponentGroup.
/// </summary>
public class ComponentGroupMemberDto
{
    /// <summary>
    /// Local ID within this group for referencing in connections.
    /// </summary>
    public int LocalId { get; set; }

    /// <summary>
    /// Template name from ComponentTemplates.
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
    /// Component rotation (0, 1, 2, 3 for 0°, 90°, 180°, 270°).
    /// </summary>
    public int Rotation { get; set; }

    /// <summary>
    /// Parameter values for parametric components.
    /// </summary>
    public Dictionary<string, double> Parameters { get; set; } = new();
}

/// <summary>
/// DTO for a connection between components in a group.
/// </summary>
public class GroupConnectionDto
{
    /// <summary>
    /// LocalId of the source component.
    /// </summary>
    public int SourceComponentId { get; set; }

    /// <summary>
    /// Pin name on the source component.
    /// </summary>
    public string SourcePinName { get; set; } = "";

    /// <summary>
    /// LocalId of the target component.
    /// </summary>
    public int TargetComponentId { get; set; }

    /// <summary>
    /// Pin name on the target component.
    /// </summary>
    public string TargetPinName { get; set; } = "";
}
