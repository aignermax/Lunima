using System.Text.Json;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;

namespace CAP_Core.Components.Creation;

/// <summary>
/// Self-contained serializer for ComponentGroup templates.
/// Unlike ComponentGroupSerializer in CAP-DataAccess which uses external component references,
/// this serializer embeds all child component data inline for independent storage and retrieval.
/// </summary>
public static class GroupTemplateSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Serializes a ComponentGroup to a self-contained JSON string.
    /// All child components, frozen paths, and external pins are embedded inline.
    /// </summary>
    public static string Serialize(ComponentGroup group)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));

        var dto = ToDto(group);
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    /// <summary>
    /// Deserializes a ComponentGroup from a self-contained JSON string.
    /// </summary>
    public static ComponentGroup? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var dto = JsonSerializer.Deserialize<GroupTemplateDto>(json, JsonOptions);
        return dto == null ? null : FromDto(dto);
    }

    /// <summary>
    /// Converts a ComponentGroup to a self-contained DTO.
    /// </summary>
    private static GroupTemplateDto ToDto(ComponentGroup group)
    {
        var dto = new GroupTemplateDto
        {
            GroupName = group.GroupName,
            Description = group.Description,
            Identifier = group.Identifier,
            PhysicalX = group.PhysicalX,
            PhysicalY = group.PhysicalY,
            WidthMicrometers = group.WidthMicrometers,
            HeightMicrometers = group.HeightMicrometers,
            Rotation = (int)group.Rotation90CounterClock
        };

        // Serialize child components inline
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup childGroup)
            {
                dto.Children.Add(new ChildComponentDto
                {
                    IsGroup = true,
                    NestedGroup = ToDto(childGroup)
                });
            }
            else
            {
                dto.Children.Add(SerializeComponent(child));
            }
        }

        // Serialize frozen paths (reference children by index)
        foreach (var path in group.InternalPaths)
        {
            dto.InternalPaths.Add(SerializeFrozenPath(path, group.ChildComponents));
        }

        // Serialize external pins (reference children by index)
        foreach (var pin in group.ExternalPins)
        {
            dto.ExternalPins.Add(SerializeGroupPin(pin, group.ChildComponents));
        }

        return dto;
    }

    /// <summary>
    /// Reconstructs a ComponentGroup from a self-contained DTO.
    /// </summary>
    private static ComponentGroup FromDto(GroupTemplateDto dto)
    {
        var group = new ComponentGroup(dto.GroupName)
        {
            Description = dto.Description,
            Identifier = dto.Identifier,
            PhysicalX = dto.PhysicalX,
            PhysicalY = dto.PhysicalY,
            WidthMicrometers = dto.WidthMicrometers,
            HeightMicrometers = dto.HeightMicrometers
        };

        // Deserialize child components
        foreach (var childDto in dto.Children)
        {
            Component child;
            if (childDto.IsGroup && childDto.NestedGroup != null)
            {
                child = FromDto(childDto.NestedGroup);
            }
            else
            {
                child = DeserializeComponent(childDto);
            }
            group.AddChild(child);
        }

        // Deserialize frozen paths
        foreach (var pathDto in dto.InternalPaths)
        {
            var frozenPath = DeserializeFrozenPath(pathDto, group.ChildComponents);
            if (frozenPath != null)
            {
                group.AddInternalPath(frozenPath);
            }
        }

        // Deserialize external pins
        foreach (var pinDto in dto.ExternalPins)
        {
            var groupPin = DeserializeGroupPin(pinDto, group.ChildComponents);
            if (groupPin != null)
            {
                group.AddExternalPin(groupPin);
            }
        }

        return group;
    }

    /// <summary>
    /// Serializes a regular Component to a DTO with all data inline.
    /// </summary>
    private static ChildComponentDto SerializeComponent(Component comp)
    {
        var pins = comp.PhysicalPins.Select(p => new PinDto
        {
            Name = p.Name,
            OffsetX = p.OffsetXMicrometers,
            OffsetY = p.OffsetYMicrometers,
            AngleDegrees = p.AngleDegrees
        }).ToList();

        return new ChildComponentDto
        {
            IsGroup = false,
            Identifier = comp.Identifier,
            HumanReadableName = comp.HumanReadableName,
            NazcaFunctionName = comp.NazcaFunctionName,
            NazcaFunctionParameters = comp.NazcaFunctionParameters,
            NazcaModuleName = comp.NazcaModuleName,
            TypeNumber = comp.TypeNumber,
            PhysicalX = comp.PhysicalX,
            PhysicalY = comp.PhysicalY,
            WidthMicrometers = comp.WidthMicrometers,
            HeightMicrometers = comp.HeightMicrometers,
            Rotation = (int)comp.Rotation90CounterClock,
            Pins = pins
        };
    }

    /// <summary>
    /// Deserializes a regular Component from a DTO.
    /// </summary>
    private static Component DeserializeComponent(ChildComponentDto dto)
    {
        var physicalPins = dto.Pins.Select(p => new PhysicalPin
        {
            Name = p.Name,
            OffsetXMicrometers = p.OffsetX,
            OffsetYMicrometers = p.OffsetY,
            AngleDegrees = p.AngleDegrees
        }).ToList();

        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            dto.NazcaFunctionName ?? "",
            dto.NazcaFunctionParameters ?? "",
            new Part[1, 1] { { new Part() } },
            dto.TypeNumber,
            dto.Identifier ?? $"comp_{Guid.NewGuid():N}",
            (DiscreteRotation)dto.Rotation,
            physicalPins)
        {
            PhysicalX = dto.PhysicalX,
            PhysicalY = dto.PhysicalY,
            WidthMicrometers = dto.WidthMicrometers,
            HeightMicrometers = dto.HeightMicrometers,
            NazcaModuleName = dto.NazcaModuleName,
            HumanReadableName = dto.HumanReadableName
        };
    }

    /// <summary>
    /// Serializes a frozen path, referencing child components by index.
    /// </summary>
    private static FrozenPathDto SerializeFrozenPath(
        FrozenWaveguidePath path,
        List<Component> children)
    {
        int startIdx = children.IndexOf(path.StartPin.ParentComponent);
        int endIdx = children.IndexOf(path.EndPin.ParentComponent);

        var segments = path.Path.Segments.Select(seg =>
        {
            if (seg is BendSegment bend)
            {
                return new SegmentDto
                {
                    Type = "arc",
                    StartX = bend.StartPoint.X,
                    StartY = bend.StartPoint.Y,
                    EndX = bend.EndPoint.X,
                    EndY = bend.EndPoint.Y,
                    StartAngleDegrees = bend.StartAngleDegrees,
                    CenterX = bend.Center.X,
                    CenterY = bend.Center.Y,
                    RadiusMicrometers = bend.RadiusMicrometers,
                    SweepAngleDegrees = bend.SweepAngleDegrees
                };
            }

            return new SegmentDto
            {
                Type = "straight",
                StartX = seg.StartPoint.X,
                StartY = seg.StartPoint.Y,
                EndX = seg.EndPoint.X,
                EndY = seg.EndPoint.Y,
                StartAngleDegrees = seg.StartAngleDegrees
            };
        }).ToList();

        return new FrozenPathDto
        {
            StartChildIndex = startIdx,
            StartPinName = path.StartPin.Name,
            EndChildIndex = endIdx,
            EndPinName = path.EndPin.Name,
            IsBlockedFallback = path.Path.IsBlockedFallback,
            IsInvalidGeometry = path.Path.IsInvalidGeometry,
            Segments = segments
        };
    }

    /// <summary>
    /// Deserializes a frozen path, resolving child references by index.
    /// </summary>
    private static FrozenWaveguidePath? DeserializeFrozenPath(
        FrozenPathDto dto,
        List<Component> children)
    {
        if (dto.StartChildIndex < 0 || dto.StartChildIndex >= children.Count)
            return null;
        if (dto.EndChildIndex < 0 || dto.EndChildIndex >= children.Count)
            return null;

        var startComp = children[dto.StartChildIndex];
        var endComp = children[dto.EndChildIndex];

        var startPin = startComp.PhysicalPins.FirstOrDefault(p => p.Name == dto.StartPinName);
        var endPin = endComp.PhysicalPins.FirstOrDefault(p => p.Name == dto.EndPinName);

        if (startPin == null || endPin == null)
            return null;

        var routedPath = new RoutedPath
        {
            IsBlockedFallback = dto.IsBlockedFallback,
            IsInvalidGeometry = dto.IsInvalidGeometry
        };

        foreach (var seg in dto.Segments)
        {
            if (seg.Type == "arc")
            {
                routedPath.Segments.Add(new BendSegment(
                    seg.CenterX, seg.CenterY,
                    seg.RadiusMicrometers,
                    seg.StartAngleDegrees,
                    seg.SweepAngleDegrees));
            }
            else
            {
                routedPath.Segments.Add(new StraightSegment(
                    seg.StartX, seg.StartY,
                    seg.EndX, seg.EndY,
                    seg.StartAngleDegrees));
            }
        }

        return new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = routedPath,
            StartPin = startPin,
            EndPin = endPin
        };
    }

    /// <summary>
    /// Serializes a group pin, referencing internal component by child index.
    /// </summary>
    private static ExternalPinDto SerializeGroupPin(
        GroupPin pin,
        List<Component> children)
    {
        int childIdx = children.IndexOf(pin.InternalPin.ParentComponent);

        return new ExternalPinDto
        {
            Name = pin.Name,
            ChildIndex = childIdx,
            InternalPinName = pin.InternalPin.Name,
            RelativeX = pin.RelativeX,
            RelativeY = pin.RelativeY,
            AngleDegrees = pin.AngleDegrees
        };
    }

    /// <summary>
    /// Deserializes a group pin, resolving internal component by child index.
    /// </summary>
    private static GroupPin? DeserializeGroupPin(
        ExternalPinDto dto,
        List<Component> children)
    {
        if (dto.ChildIndex < 0 || dto.ChildIndex >= children.Count)
            return null;

        var internalComp = children[dto.ChildIndex];
        var internalPin = internalComp.PhysicalPins.FirstOrDefault(
            p => p.Name == dto.InternalPinName);

        if (internalPin == null)
            return null;

        return new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = dto.Name,
            InternalPin = internalPin,
            RelativeX = dto.RelativeX,
            RelativeY = dto.RelativeY,
            AngleDegrees = dto.AngleDegrees
        };
    }
}

#region DTOs for self-contained group template serialization

/// <summary>
/// Self-contained DTO for a ComponentGroup template.
/// All data needed for reconstruction is embedded inline.
/// </summary>
public class GroupTemplateDto
{
    public string GroupName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Identifier { get; set; } = "";
    public double PhysicalX { get; set; }
    public double PhysicalY { get; set; }
    public double WidthMicrometers { get; set; }
    public double HeightMicrometers { get; set; }
    public int Rotation { get; set; }
    public List<ChildComponentDto> Children { get; set; } = new();
    public List<FrozenPathDto> InternalPaths { get; set; } = new();
    public List<ExternalPinDto> ExternalPins { get; set; } = new();
}

/// <summary>
/// DTO for a child component (regular or nested group).
/// </summary>
public class ChildComponentDto
{
    public bool IsGroup { get; set; }
    public string? Identifier { get; set; }

    /// <summary>
    /// Human-readable display name, separate from Identifier.
    /// </summary>
    public string? HumanReadableName { get; set; }

    public string? NazcaFunctionName { get; set; }
    public string? NazcaFunctionParameters { get; set; }
    public string? NazcaModuleName { get; set; }
    public int TypeNumber { get; set; }
    public double PhysicalX { get; set; }
    public double PhysicalY { get; set; }
    public double WidthMicrometers { get; set; }
    public double HeightMicrometers { get; set; }
    public int Rotation { get; set; }
    public List<PinDto> Pins { get; set; } = new();
    public GroupTemplateDto? NestedGroup { get; set; }
}

/// <summary>
/// DTO for a physical pin on a child component.
/// </summary>
public class PinDto
{
    public string Name { get; set; } = "";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double AngleDegrees { get; set; }
}

/// <summary>
/// DTO for a frozen waveguide path within the group.
/// References child components by index in the Children list.
/// </summary>
public class FrozenPathDto
{
    public int StartChildIndex { get; set; }
    public string StartPinName { get; set; } = "";
    public int EndChildIndex { get; set; }
    public string EndPinName { get; set; } = "";
    public bool IsBlockedFallback { get; set; }
    public bool IsInvalidGeometry { get; set; }
    public List<SegmentDto> Segments { get; set; } = new();
}

/// <summary>
/// DTO for a path segment (straight or arc).
/// </summary>
public class SegmentDto
{
    public string Type { get; set; } = "straight";
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double StartAngleDegrees { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RadiusMicrometers { get; set; }
    public double SweepAngleDegrees { get; set; }
}

/// <summary>
/// DTO for an external pin exposed by the group.
/// References internal component by index in the Children list.
/// </summary>
public class ExternalPinDto
{
    public string Name { get; set; } = "";
    public int ChildIndex { get; set; }
    public string InternalPinName { get; set; } = "";
    public double RelativeX { get; set; }
    public double RelativeY { get; set; }
    public double AngleDegrees { get; set; }
}

#endregion
