using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CAP_Core.LightCalculation;

namespace CAP_Core.Components.Core;

/// <summary>
/// Represents a simple group of components on the canvas.
/// Groups are just bounding boxes with a child list — no frozen waveguides,
/// no edit mode, no external pins. All waveguides are always recalculated.
/// </summary>
public class ComponentGroup : Component, INotifyPropertyChanged
{
    /// <summary>
    /// Child components contained in this group.
    /// </summary>
    public List<Component> ChildComponents { get; private set; } = new();

    /// <summary>
    /// Human-readable name for this group.
    /// </summary>
    private string _groupName = "";
    public string GroupName
    {
        get => _groupName;
        set
        {
            if (_groupName != value)
            {
                _groupName = value;
                OnPropertyChanged();
                UpdateLabelBoundsAfterNameChange();
            }
        }
    }

    /// <summary>
    /// Optional description of this group's purpose.
    /// </summary>
    private string _description = "";
    public string Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Event raised when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Reference to parent group if this group is nested within another group.
    /// Null if this is a top-level group.
    /// </summary>
    [JsonIgnore]
    public new ComponentGroup? ParentGroup { get; set; }

    /// <summary>
    /// Bounding rectangle for the group label (used for hit testing).
    /// Updated when the group bounds change.
    /// </summary>
    [JsonIgnore]
    public (double X, double Y, double Width, double Height) LabelBounds { get; private set; }

    /// <summary>
    /// Creates an empty ComponentGroup.
    /// Use <see cref="AddChild"/> to populate the group.
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
        _groupName = groupName;
        _description = "";
    }

    /// <summary>
    /// Adds a child component to this group.
    /// </summary>
    /// <param name="component">Component to add to the group.</param>
    public void AddChild(Component component)
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));

        if (ChildComponents.Contains(component))
            throw new InvalidOperationException("Component is already a child of this group.");

        component.ParentGroup = this;

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
    /// Moves the entire group and all children by the specified delta.
    /// Waveguides are recalculated externally after the move.
    /// </summary>
    /// <param name="deltaX">X offset in micrometers.</param>
    /// <param name="deltaY">Y offset in micrometers.</param>
    public void MoveGroup(double deltaX, double deltaY)
    {
        PhysicalX += deltaX;
        PhysicalY += deltaY;

        foreach (var child in ChildComponents)
        {
            child.PhysicalX += deltaX;
            child.PhysicalY += deltaY;

            if (child is ComponentGroup childGroup)
            {
                MoveChildrenRecursively(childGroup, deltaX, deltaY);
            }
        }

        var (labelX, labelY, labelWidth, labelHeight) = LabelBounds;
        LabelBounds = (labelX + deltaX, labelY + deltaY, labelWidth, labelHeight);
    }

    /// <summary>
    /// Recursively moves all children of a nested group.
    /// </summary>
    private static void MoveChildrenRecursively(ComponentGroup group, double deltaX, double deltaY)
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
    }

    /// <summary>
    /// Rotates the entire group and all children by 90 degrees counter-clockwise.
    /// </summary>
    public void RotateGroupBy90CounterClockwise()
    {
        RotateBy90CounterClockwise();

        double groupX = PhysicalX;
        double groupY = PhysicalY;

        foreach (var child in ChildComponents)
        {
            double relX = child.PhysicalX - groupX;
            double relY = child.PhysicalY - groupY;

            // Rotate 90° counter-clockwise: (x, y) → (-y, x)
            child.PhysicalX = groupX + (-relY);
            child.PhysicalY = groupY + relX;

            child.RotateBy90CounterClockwise();
        }

        UpdateGroupBounds();
    }

    /// <summary>
    /// Updates the group's bounding box based on child components.
    /// Also updates the label bounds for hit testing.
    /// </summary>
    public void UpdateGroupBounds()
    {
        if (ChildComponents.Count == 0)
        {
            WidthMicrometers = 0;
            HeightMicrometers = 0;
            LabelBounds = (PhysicalX, PhysicalY, 0, 0);
            return;
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (var child in ChildComponents)
        {
            minX = Math.Min(minX, child.PhysicalX);
            minY = Math.Min(minY, child.PhysicalY);
            maxX = Math.Max(maxX, child.PhysicalX + child.WidthMicrometers);
            maxY = Math.Max(maxY, child.PhysicalY + child.HeightMicrometers);
        }

        WidthMicrometers = maxX - minX;
        HeightMicrometers = maxY - minY;

        UpdateLabelBounds(minX, minY);
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
    /// Creates a deep copy of this ComponentGroup with new unique identifiers.
    /// </summary>
    public override object Clone()
    {
        return DeepCopy();
    }

    /// <summary>
    /// Creates a deep copy of this ComponentGroup with new unique identifiers.
    /// </summary>
    public ComponentGroup DeepCopy()
    {
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

        foreach (var child in ChildComponents)
        {
            Component clonedChild;
            if (child is ComponentGroup childGroup)
            {
                clonedChild = childGroup.DeepCopy();
            }
            else
            {
                clonedChild = CloneComponent(child);
            }

            newGroup.AddChild(clonedChild);
        }

        return newGroup;
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Updates the label bounds based on group name and position.
    /// </summary>
    private void UpdateLabelBounds(double groupMinX, double groupMinY)
    {
        const double BorderPadding = 10.0;
        const double LabelHeight = 18.0;

        double estimatedLabelWidth = Math.Max(60, GroupName.Length * 7 + 8);

        double labelX = groupMinX - BorderPadding;
        double labelY = groupMinY - BorderPadding - 20;

        LabelBounds = (labelX, labelY, estimatedLabelWidth, LabelHeight);
    }

    /// <summary>
    /// Updates label bounds when the group name changes.
    /// </summary>
    private void UpdateLabelBoundsAfterNameChange()
    {
        if (ChildComponents.Count > 0)
        {
            UpdateGroupBounds();
        }
    }

    /// <summary>
    /// Creates a shallow clone of a Component with a new unique identifier.
    /// </summary>
    private static Component CloneComponent(Component source)
    {
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
            IsLocked = false
        };

        return cloned;
    }
}
