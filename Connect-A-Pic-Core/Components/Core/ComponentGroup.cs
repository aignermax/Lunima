using System.Text.Json.Serialization;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;

namespace CAP_Core.Components.Core;

/// <summary>
/// Represents a hierarchical group of components with frozen waveguide paths.
/// Follows IPKISS-style transparent hierarchy where groups contain child components
/// at fixed relative positions with frozen waveguide paths.
/// </summary>
public class ComponentGroup : Component
{
    /// <summary>
    /// Child components contained in this group with relative positions.
    /// </summary>
    public List<Component> ChildComponents { get; private set; } = new();

    /// <summary>
    /// Frozen waveguide paths between child components (don't recalculate during group moves).
    /// </summary>
    public List<FrozenWaveguidePath> InternalPaths { get; private set; } = new();

    /// <summary>
    /// External pins exposed by this group for connections to outside components.
    /// </summary>
    public List<GroupPin> ExternalPins { get; private set; } = new();

    /// <summary>
    /// Human-readable name for this group.
    /// </summary>
    public string GroupName { get; set; }

    /// <summary>
    /// Optional description of this group's purpose.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Reference to parent group if this group is nested within another group.
    /// Null if this is a top-level group.
    /// </summary>
    [JsonIgnore]
    public new ComponentGroup? ParentGroup { get; set; }

    /// <summary>
    /// Creates an empty ComponentGroup with default S-matrices.
    /// Use AddChild() and AddInternalPath() to populate the group.
    /// </summary>
    public ComponentGroup(string groupName = "Unnamed Group")
        : base(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "group",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            $"group_{Guid.NewGuid():N}",
            new DiscreteRotation(),
            new List<PhysicalPin>()
        )
    {
        GroupName = groupName;
        Description = "";
    }

    /// <summary>
    /// Adds a child component to this group.
    /// The child's position is stored as relative to the group origin.
    /// </summary>
    /// <param name="component">Component to add to the group.</param>
    public void AddChild(Component component)
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));

        if (ChildComponents.Contains(component))
            throw new InvalidOperationException("Component is already a child of this group.");

        component.ParentGroup = this;

        // If the component is itself a group, also set its typed ParentGroup property
        if (component is ComponentGroup childGroup)
        {
            childGroup.ParentGroup = this;
        }

        ChildComponents.Add(component);
        UpdateGroupBounds();
    }

    /// <summary>
    /// Removes a child component from this group.
    /// </summary>
    /// <param name="component">Component to remove.</param>
    /// <returns>True if the component was removed, false if it wasn't in the group.</returns>
    public bool RemoveChild(Component component)
    {
        if (component == null) return false;

        bool removed = ChildComponents.Remove(component);
        if (removed)
        {
            component.ParentGroup = null;
            UpdateGroupBounds();
        }
        return removed;
    }

    /// <summary>
    /// Adds a frozen waveguide path between two child components.
    /// </summary>
    /// <param name="path">The frozen path to add.</param>
    public void AddInternalPath(FrozenWaveguidePath path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        InternalPaths.Add(path);
    }

    /// <summary>
    /// Removes a frozen waveguide path from this group.
    /// </summary>
    /// <param name="path">The path to remove.</param>
    /// <returns>True if the path was removed.</returns>
    public bool RemoveInternalPath(FrozenWaveguidePath path)
    {
        return InternalPaths.Remove(path);
    }

    /// <summary>
    /// Adds an external pin that exposes an internal component's pin.
    /// </summary>
    /// <param name="pin">The group pin to add.</param>
    public void AddExternalPin(GroupPin pin)
    {
        if (pin == null)
            throw new ArgumentNullException(nameof(pin));

        ExternalPins.Add(pin);
    }

    /// <summary>
    /// Moves the entire group and all children by the specified delta.
    /// Internal waveguide paths are translated, not re-routed.
    /// </summary>
    /// <param name="deltaX">X offset in micrometers.</param>
    /// <param name="deltaY">Y offset in micrometers.</param>
    public void MoveGroup(double deltaX, double deltaY)
    {
        // Move group origin
        PhysicalX += deltaX;
        PhysicalY += deltaY;

        // Move all children (they use absolute positions that need to be offset)
        foreach (var child in ChildComponents)
        {
            child.PhysicalX += deltaX;
            child.PhysicalY += deltaY;

            // If child is also a group, recursively move its descendants
            if (child is ComponentGroup childGroup)
            {
                // Recursively move all descendants of the child group
                MoveChildrenRecursively(childGroup, deltaX, deltaY);
            }
        }

        // Translate frozen paths without re-routing
        foreach (var path in InternalPaths)
        {
            path.TranslateBy(deltaX, deltaY);
        }
    }

    /// <summary>
    /// Recursively moves all children of a group without moving the group itself.
    /// Used when a nested group's position has already been updated.
    /// </summary>
    private void MoveChildrenRecursively(ComponentGroup group, double deltaX, double deltaY)
    {
        foreach (var child in group.ChildComponents)
        {
            child.PhysicalX += deltaX;
            child.PhysicalY += deltaY;

            if (child is ComponentGroup childGroup)
            {
                MoveChildrenRecursively(childGroup, deltaX, deltaY);
            }
        }

        // Also translate the group's internal paths
        foreach (var path in group.InternalPaths)
        {
            path.TranslateBy(deltaX, deltaY);
        }
    }

    /// <summary>
    /// Rotates the entire group and all children by 90 degrees counter-clockwise.
    /// Updates child positions and frozen path geometries.
    /// </summary>
    public void RotateGroupBy90CounterClockwise()
    {
        // Rotate the group itself
        RotateBy90CounterClockwise();

        double groupX = PhysicalX;
        double groupY = PhysicalY;

        // Rotate each child around the group origin
        foreach (var child in ChildComponents)
        {
            // Calculate relative position
            double relX = child.PhysicalX - groupX;
            double relY = child.PhysicalY - groupY;

            // Rotate 90° counter-clockwise: (x, y) → (-y, x)
            double newRelX = -relY;
            double newRelY = relX;

            // Update child position
            child.PhysicalX = groupX + newRelX;
            child.PhysicalY = groupY + newRelY;

            // Rotate the child component itself
            child.RotateBy90CounterClockwise();
        }

        // Rotate frozen paths (each segment needs rotation around group origin)
        foreach (var frozenPath in InternalPaths)
        {
            RotatePathSegmentsBy90(frozenPath, groupX, groupY);
        }

        UpdateGroupBounds();
    }

    /// <summary>
    /// Rotates all segments in a frozen path by 90 degrees counter-clockwise around a center point.
    /// </summary>
    private void RotatePathSegmentsBy90(FrozenWaveguidePath frozenPath, double centerX, double centerY)
    {
        if (frozenPath?.Path?.Segments == null) return;

        foreach (var segment in frozenPath.Path.Segments)
        {
            segment.StartPoint = RotatePoint(segment.StartPoint.X, segment.StartPoint.Y, centerX, centerY);
            segment.EndPoint = RotatePoint(segment.EndPoint.X, segment.EndPoint.Y, centerX, centerY);

            if (segment is BendSegment bend)
            {
                bend.Center = RotatePoint(bend.Center.X, bend.Center.Y, centerX, centerY);
            }
        }
    }

    /// <summary>
    /// Rotates a point by 90 degrees counter-clockwise around a center.
    /// </summary>
    private (double X, double Y) RotatePoint(double x, double y, double centerX, double centerY)
    {
        double relX = x - centerX;
        double relY = y - centerY;

        // Rotate 90° counter-clockwise: (x, y) → (-y, x)
        double newRelX = -relY;
        double newRelY = relX;

        return (centerX + newRelX, centerY + newRelY);
    }

    /// <summary>
    /// Updates the group's bounding box based on child components.
    /// </summary>
    private void UpdateGroupBounds()
    {
        if (ChildComponents.Count == 0)
        {
            WidthMicrometers = 0;
            HeightMicrometers = 0;
            return;
        }

        double minX = ChildComponents.Min(c => c.PhysicalX);
        double minY = ChildComponents.Min(c => c.PhysicalY);
        double maxX = ChildComponents.Max(c => c.PhysicalX + c.WidthMicrometers);
        double maxY = ChildComponents.Max(c => c.PhysicalY + c.HeightMicrometers);

        WidthMicrometers = maxX - minX;
        HeightMicrometers = maxY - minY;
    }

    /// <summary>
    /// Gets all components recursively (including nested group children).
    /// </summary>
    public List<Component> GetAllComponentsRecursive()
    {
        var allComponents = new List<Component>();
        foreach (var child in ChildComponents)
        {
            allComponents.Add(child);
            if (child is ComponentGroup childGroup)
            {
                allComponents.AddRange(childGroup.GetAllComponentsRecursive());
            }
        }
        return allComponents;
    }

    /// <summary>
    /// Creates a deep copy of this ComponentGroup with new unique identifiers for all components.
    /// Used for instantiating group templates from the library.
    /// </summary>
    /// <returns>A new ComponentGroup instance with cloned children and paths.</returns>
    public ComponentGroup DeepCopy()
    {
        // Create new group with unique identifier
        var newGroup = new ComponentGroup(GroupName)
        {
            Description = Description,
            Identifier = $"group_{Guid.NewGuid():N}",
            PhysicalX = PhysicalX,
            PhysicalY = PhysicalY,
            WidthMicrometers = WidthMicrometers,
            HeightMicrometers = HeightMicrometers,
            Rotation90CounterClock = Rotation90CounterClock
        };

        // Map old component identifiers to new cloned components
        var componentMap = new Dictionary<string, Component>();

        // Deep copy child components
        foreach (var child in ChildComponents)
        {
            Component clonedChild;
            if (child is ComponentGroup childGroup)
            {
                // Recursively clone nested groups
                clonedChild = childGroup.DeepCopy();
            }
            else
            {
                // Clone regular component
                clonedChild = CloneComponent(child);
            }

            componentMap[child.Identifier] = clonedChild;
            newGroup.AddChild(clonedChild);
        }

        // Clone frozen waveguide paths
        foreach (var frozenPath in InternalPaths)
        {
            var startComp = componentMap[frozenPath.StartPin.ParentComponent.Identifier];
            var endComp = componentMap[frozenPath.EndPin.ParentComponent.Identifier];

            var newStartPin = startComp.PhysicalPins.First(p => p.Name == frozenPath.StartPin.Name);
            var newEndPin = endComp.PhysicalPins.First(p => p.Name == frozenPath.EndPin.Name);

            var clonedPath = new FrozenWaveguidePath
            {
                PathId = Guid.NewGuid(),
                Path = CloneRoutedPath(frozenPath.Path),
                StartPin = newStartPin,
                EndPin = newEndPin
            };

            newGroup.AddInternalPath(clonedPath);
        }

        // Clone external pins
        foreach (var externalPin in ExternalPins)
        {
            var internalComp = componentMap[externalPin.InternalPin.ParentComponent.Identifier];
            var newInternalPin = internalComp.PhysicalPins.First(p => p.Name == externalPin.InternalPin.Name);

            var clonedPin = new GroupPin
            {
                PinId = Guid.NewGuid(),
                Name = externalPin.Name,
                InternalPin = newInternalPin,
                RelativeX = externalPin.RelativeX,
                RelativeY = externalPin.RelativeY,
                AngleDegrees = externalPin.AngleDegrees
            };

            newGroup.AddExternalPin(clonedPin);
        }

        return newGroup;
    }

    /// <summary>
    /// Creates a shallow clone of a Component with a new unique identifier.
    /// </summary>
    private Component CloneComponent(Component source)
    {
        // Create new component with same S-matrix and structure
        var cloned = new Component(
            new Dictionary<int, SMatrix>(source.WaveLengthToSMatrixMap),
            source.GetAllSliders().Select(s => new Slider(
                Guid.NewGuid(),
                s.Number,
                s.Value,
                s.MaxValue,
                s.MinValue)).ToList(),
            source.NazcaFunctionName,
            source.NazcaFunctionParameters,
            source.Parts,
            source.TypeNumber,
            $"{source.Identifier}_{Guid.NewGuid():N}",
            source.Rotation90CounterClock,
            source.PhysicalPins.Select(p => new PhysicalPin
            {
                Name = p.Name,
                OffsetXMicrometers = p.OffsetXMicrometers,
                OffsetYMicrometers = p.OffsetYMicrometers,
                AngleDegrees = p.AngleDegrees,
                LogicalPin = p.LogicalPin
            }).ToList()
        )
        {
            PhysicalX = source.PhysicalX,
            PhysicalY = source.PhysicalY,
            WidthMicrometers = source.WidthMicrometers,
            HeightMicrometers = source.HeightMicrometers,
            NazcaOriginOffsetX = source.NazcaOriginOffsetX,
            NazcaOriginOffsetY = source.NazcaOriginOffsetY,
            NazcaModuleName = source.NazcaModuleName,
            IsLocked = false // Don't copy lock state
        };

        return cloned;
    }

    /// <summary>
    /// Creates a deep clone of a RoutedPath.
    /// </summary>
    private RoutedPath CloneRoutedPath(RoutedPath source)
    {
        var cloned = new RoutedPath
        {
            IsBlockedFallback = source.IsBlockedFallback,
            IsInvalidGeometry = source.IsInvalidGeometry
        };

        foreach (var segment in source.Segments)
        {
            if (segment is BendSegment bend)
            {
                cloned.Segments.Add(new BendSegment(
                    bend.Center.X,
                    bend.Center.Y,
                    bend.RadiusMicrometers,
                    bend.StartAngleDegrees,
                    bend.SweepAngleDegrees
                ));
            }
            else if (segment is StraightSegment straight)
            {
                cloned.Segments.Add(new StraightSegment(
                    straight.StartPoint.X,
                    straight.StartPoint.Y,
                    straight.EndPoint.X,
                    straight.EndPoint.Y,
                    straight.StartAngleDegrees
                ));
            }
        }

        return cloned;
    }
}
