using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence.DTOs;

namespace CAP_DataAccess.Persistence;

/// <summary>
/// Serializes and deserializes ComponentGroup instances to/from DTOs.
/// Handles hierarchical structure, frozen paths, and external pin mappings.
/// </summary>
public static class ComponentGroupSerializer
{
    /// <summary>
    /// Converts a ComponentGroup to a DTO for JSON serialization.
    /// </summary>
    /// <param name="group">The ComponentGroup to serialize.</param>
    /// <returns>DTO representation of the group.</returns>
    public static ComponentGroupDto ToDto(ComponentGroup group)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));

        var dto = new ComponentGroupDto
        {
            GroupName = group.GroupName,
            Description = group.Description,
            Identifier = group.Identifier,
            GridX = group.GridXMainTile,
            GridY = group.GridYMainTile,
            PhysicalX = group.PhysicalX,
            PhysicalY = group.PhysicalY,
            Rotation90CounterClock = (int)group.Rotation90CounterClock,
            ParentGroupId = group.ParentGroup?.Identifier
        };

        // Add child component IDs
        foreach (var child in group.ChildComponents)
        {
            dto.ChildComponentIds.Add(child.Identifier);
        }

        // Serialize frozen paths
        foreach (var frozenPath in group.InternalPaths)
        {
            dto.InternalPaths.Add(ToFrozenPathDto(frozenPath));
        }

        // Serialize external pins
        foreach (var pin in group.ExternalPins)
        {
            dto.ExternalPins.Add(ToGroupPinDto(pin));
        }

        return dto;
    }

    /// <summary>
    /// Reconstructs a ComponentGroup from a DTO after all individual components are loaded.
    /// </summary>
    /// <param name="dto">The DTO containing group data.</param>
    /// <param name="componentLookup">Dictionary mapping component IDs to Component instances.</param>
    /// <returns>Reconstructed ComponentGroup.</returns>
    public static ComponentGroup FromDto(
        ComponentGroupDto dto,
        Dictionary<string, Component> componentLookup)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));
        if (componentLookup == null)
            throw new ArgumentNullException(nameof(componentLookup));

        // Create the group
        var group = new ComponentGroup(dto.GroupName)
        {
            Description = dto.Description,
            Identifier = dto.Identifier,
            PhysicalX = dto.PhysicalX,
            PhysicalY = dto.PhysicalY,
            Rotation90CounterClock = (DiscreteRotation)dto.Rotation90CounterClock
        };

        // Add child components
        foreach (var childId in dto.ChildComponentIds)
        {
            if (componentLookup.TryGetValue(childId, out var childComponent))
            {
                group.AddChild(childComponent);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Child component '{childId}' not found in component lookup.");
            }
        }

        // Reconstruct frozen paths
        foreach (var pathDto in dto.InternalPaths)
        {
            var frozenPath = FromFrozenPathDto(pathDto, componentLookup);
            group.AddInternalPath(frozenPath);
        }

        // Reconstruct external pins
        foreach (var pinDto in dto.ExternalPins)
        {
            var groupPin = FromGroupPinDto(pinDto, componentLookup);
            group.AddExternalPin(groupPin);
        }

        return group;
    }

    /// <summary>
    /// Converts a FrozenWaveguidePath to a DTO.
    /// </summary>
    private static FrozenPathDto ToFrozenPathDto(FrozenWaveguidePath frozenPath)
    {
        var dto = new FrozenPathDto
        {
            PathId = frozenPath.PathId.ToString(),
            StartComponentId = frozenPath.StartPin.ParentComponent.Identifier,
            StartPinName = frozenPath.StartPin.Name,
            EndComponentId = frozenPath.EndPin.ParentComponent.Identifier,
            EndPinName = frozenPath.EndPin.Name,
            IsBlockedFallback = frozenPath.Path.IsBlockedFallback,
            IsInvalidGeometry = frozenPath.Path.IsInvalidGeometry
        };

        // Serialize path segments
        foreach (var segment in frozenPath.Path.Segments)
        {
            dto.Segments.Add(ToPathSegmentDto(segment));
        }

        return dto;
    }

    /// <summary>
    /// Reconstructs a FrozenWaveguidePath from a DTO.
    /// </summary>
    private static FrozenWaveguidePath FromFrozenPathDto(
        FrozenPathDto dto,
        Dictionary<string, Component> componentLookup)
    {
        // Find start and end pins
        if (!componentLookup.TryGetValue(dto.StartComponentId, out var startComp))
        {
            throw new InvalidOperationException(
                $"Start component '{dto.StartComponentId}' not found.");
        }

        if (!componentLookup.TryGetValue(dto.EndComponentId, out var endComp))
        {
            throw new InvalidOperationException(
                $"End component '{dto.EndComponentId}' not found.");
        }

        var startPin = startComp.PhysicalPins.FirstOrDefault(p => p.Name == dto.StartPinName);
        var endPin = endComp.PhysicalPins.FirstOrDefault(p => p.Name == dto.EndPinName);

        if (startPin == null)
        {
            throw new InvalidOperationException(
                $"Start pin '{dto.StartPinName}' not found on component '{dto.StartComponentId}'.");
        }

        if (endPin == null)
        {
            throw new InvalidOperationException(
                $"End pin '{dto.EndPinName}' not found on component '{dto.EndComponentId}'.");
        }

        // Reconstruct path
        var path = new RoutedPath
        {
            IsBlockedFallback = dto.IsBlockedFallback,
            IsInvalidGeometry = dto.IsInvalidGeometry
        };

        foreach (var segmentDto in dto.Segments)
        {
            path.Segments.Add(FromPathSegmentDto(segmentDto));
        }

        return new FrozenWaveguidePath
        {
            PathId = Guid.Parse(dto.PathId),
            Path = path,
            StartPin = startPin,
            EndPin = endPin
        };
    }

    /// <summary>
    /// Converts a PathSegment to a DTO.
    /// </summary>
    private static PathSegmentDto ToPathSegmentDto(PathSegment segment)
    {
        var dto = new PathSegmentDto
        {
            StartX = segment.StartPoint.X,
            StartY = segment.StartPoint.Y,
            EndX = segment.EndPoint.X,
            EndY = segment.EndPoint.Y,
            StartAngleDegrees = segment.StartAngleDegrees,
            EndAngleDegrees = segment.EndAngleDegrees
        };

        if (segment is BendSegment bend)
        {
            dto.Type = "arc";
            dto.CenterX = bend.Center.X;
            dto.CenterY = bend.Center.Y;
            dto.RadiusMicrometers = bend.RadiusMicrometers;
            dto.SweepAngleDegrees = bend.SweepAngleDegrees;
        }
        else
        {
            dto.Type = "straight";
        }

        return dto;
    }

    /// <summary>
    /// Reconstructs a PathSegment from a DTO.
    /// </summary>
    private static PathSegment FromPathSegmentDto(PathSegmentDto dto)
    {
        if (dto.Type == "arc")
        {
            return new BendSegment(
                dto.CenterX ?? 0,
                dto.CenterY ?? 0,
                dto.RadiusMicrometers ?? 0,
                dto.StartAngleDegrees,
                dto.SweepAngleDegrees ?? 0
            );
        }

        return new StraightSegment(
            dto.StartX,
            dto.StartY,
            dto.EndX,
            dto.EndY,
            dto.StartAngleDegrees
        );
    }

    /// <summary>
    /// Converts a GroupPin to a DTO.
    /// </summary>
    private static GroupPinDto ToGroupPinDto(GroupPin pin)
    {
        return new GroupPinDto
        {
            PinId = pin.PinId.ToString(),
            Name = pin.Name,
            InternalComponentId = pin.InternalPin.ParentComponent.Identifier,
            InternalPinName = pin.InternalPin.Name,
            RelativeX = pin.RelativeX,
            RelativeY = pin.RelativeY,
            AngleDegrees = pin.AngleDegrees
        };
    }

    /// <summary>
    /// Reconstructs a GroupPin from a DTO.
    /// </summary>
    private static GroupPin FromGroupPinDto(
        GroupPinDto dto,
        Dictionary<string, Component> componentLookup)
    {
        if (!componentLookup.TryGetValue(dto.InternalComponentId, out var internalComp))
        {
            throw new InvalidOperationException(
                $"Internal component '{dto.InternalComponentId}' not found for group pin.");
        }

        var internalPin = internalComp.PhysicalPins.FirstOrDefault(p => p.Name == dto.InternalPinName);
        if (internalPin == null)
        {
            throw new InvalidOperationException(
                $"Internal pin '{dto.InternalPinName}' not found on component '{dto.InternalComponentId}'.");
        }

        return new GroupPin
        {
            PinId = Guid.Parse(dto.PinId),
            Name = dto.Name,
            InternalPin = internalPin,
            RelativeX = dto.RelativeX,
            RelativeY = dto.RelativeY,
            AngleDegrees = dto.AngleDegrees
        };
    }
}
