using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Persistence;

/// <summary>
/// Handles serialization and deserialization of ComponentGroup objects.
/// </summary>
public class ComponentGroupSerializer
{
    /// <summary>
    /// Converts a ComponentGroup to a DTO for JSON serialization.
    /// </summary>
    public ComponentGroupDto ToDto(ComponentGroup group)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));

        return new ComponentGroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Category = group.Category,
            Description = group.Description,
            WidthMicrometers = group.WidthMicrometers,
            HeightMicrometers = group.HeightMicrometers,
            CreatedAt = group.CreatedAt,
            ModifiedAt = group.ModifiedAt,
            Components = group.Components.Select(ToDto).ToList(),
            Connections = group.Connections.Select(ToDto).ToList()
        };
    }

    /// <summary>
    /// Converts a DTO back to a ComponentGroup.
    /// </summary>
    public ComponentGroup FromDto(ComponentGroupDto dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        return new ComponentGroup
        {
            Id = dto.Id,
            Name = dto.Name,
            Category = dto.Category,
            Description = dto.Description,
            WidthMicrometers = dto.WidthMicrometers,
            HeightMicrometers = dto.HeightMicrometers,
            CreatedAt = dto.CreatedAt,
            ModifiedAt = dto.ModifiedAt,
            Components = dto.Components.Select(FromDto).ToList(),
            Connections = dto.Connections.Select(FromDto).ToList()
        };
    }

    /// <summary>
    /// Converts a ComponentGroupMember to a DTO.
    /// </summary>
    private ComponentGroupMemberDto ToDto(ComponentGroupMember member)
    {
        return new ComponentGroupMemberDto
        {
            LocalId = member.LocalId,
            TemplateName = member.TemplateName,
            RelativeX = member.RelativeX,
            RelativeY = member.RelativeY,
            Rotation = (int)member.Rotation,
            Parameters = new Dictionary<string, double>(member.Parameters)
        };
    }

    /// <summary>
    /// Converts a DTO back to a ComponentGroupMember.
    /// </summary>
    private ComponentGroupMember FromDto(ComponentGroupMemberDto dto)
    {
        return new ComponentGroupMember
        {
            LocalId = dto.LocalId,
            TemplateName = dto.TemplateName,
            RelativeX = dto.RelativeX,
            RelativeY = dto.RelativeY,
            Rotation = (DiscreteRotation)dto.Rotation,
            Parameters = new Dictionary<string, double>(dto.Parameters)
        };
    }

    /// <summary>
    /// Converts a GroupConnection to a DTO.
    /// </summary>
    private GroupConnectionDto ToDto(GroupConnection connection)
    {
        return new GroupConnectionDto
        {
            SourceComponentId = connection.SourceComponentId,
            SourcePinName = connection.SourcePinName,
            TargetComponentId = connection.TargetComponentId,
            TargetPinName = connection.TargetPinName
        };
    }

    /// <summary>
    /// Converts a DTO back to a GroupConnection.
    /// </summary>
    private GroupConnection FromDto(GroupConnectionDto dto)
    {
        return new GroupConnection
        {
            SourceComponentId = dto.SourceComponentId,
            SourcePinName = dto.SourcePinName,
            TargetComponentId = dto.TargetComponentId,
            TargetPinName = dto.TargetPinName
        };
    }
}
