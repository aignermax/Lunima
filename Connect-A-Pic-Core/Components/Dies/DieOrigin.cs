using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>
/// Turns a group into a die whose origin is the outline's top-left corner. Keeping the
/// origin on the outline makes the group's canvas position, its stored position and its
/// outline one and the same point, so moving, saving and snapping a die never has to
/// account for an offset. Children keep their absolute positions; only the group origin
/// and the origin-relative external pin offsets shift.
/// </summary>
public static class DieOrigin
{
    /// <summary>
    /// Assigns <paramref name="die"/> to <paramref name="group"/>, moving the group origin
    /// onto the outline's top-left corner (the stored outline offset becomes zero).
    /// </summary>
    public static void Assign(ComponentGroup group, DieDefinition die)
    {
        double dx = die.OutlineOffsetXMicrometers;
        double dy = die.OutlineOffsetYMicrometers;
        if (dx != 0 || dy != 0)
            ShiftOrigin(group, dx, dy);
        group.Die = die with { OutlineOffsetXMicrometers = 0, OutlineOffsetYMicrometers = 0 };
    }

    private static void ShiftOrigin(ComponentGroup group, double dx, double dy)
    {
        group.PhysicalX += dx;
        group.PhysicalY += dy;
        foreach (var pin in group.ExternalPins)
        {
            pin.RelativeX -= dx;
            pin.RelativeY -= dy;
        }
        foreach (var pin in group.PhysicalPins)
        {
            pin.OffsetXMicrometers -= dx;
            pin.OffsetYMicrometers -= dy;
        }
    }
}
