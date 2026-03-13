namespace CAP_Core.Components.Core;

/// <summary>
/// External pin exposed by a ComponentGroup.
/// Maps to an internal component's pin, allowing external waveguide connections to the group.
/// </summary>
public class GroupPin : ICloneable
{
    /// <summary>
    /// External name of this group pin (visible to outside connections).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Reference to the internal component's physical pin that this group pin exposes.
    /// </summary>
    public PhysicalPin InternalPin { get; set; }

    /// <summary>
    /// X position relative to the group's origin (micrometers).
    /// </summary>
    public double RelativeX { get; set; }

    /// <summary>
    /// Y position relative to the group's origin (micrometers).
    /// </summary>
    public double RelativeY { get; set; }

    /// <summary>
    /// Pin angle in degrees (0° = east, 90° = north, etc.).
    /// </summary>
    public double AngleDegrees { get; set; }

    /// <summary>
    /// Unique identifier for this group pin.
    /// </summary>
    public Guid PinId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Creates a clone of this GroupPin with a new ID.
    /// </summary>
    public object Clone()
    {
        return new GroupPin
        {
            Name = Name,
            RelativeX = RelativeX,
            RelativeY = RelativeY,
            AngleDegrees = AngleDegrees,
            PinId = Guid.NewGuid(),
            // InternalPin reference must be updated after cloning by the ComponentGroup
        };
    }
}
