using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.ComponentHelpers;

/// <summary>
/// Provides utilities to determine if GroupPins are occupied by waveguide connections.
/// A GroupPin is considered occupied if any connection uses its InternalPin.
/// </summary>
public static class GroupPinOccupancyChecker
{
    /// <summary>
    /// Checks if a GroupPin is currently occupied by any waveguide connection.
    /// </summary>
    /// <param name="groupPin">The group pin to check.</param>
    /// <param name="allConnections">All waveguide connections in the design.</param>
    /// <returns>True if the pin is occupied, false otherwise.</returns>
    public static bool IsOccupied(GroupPin groupPin, IEnumerable<WaveguideConnection> allConnections)
    {
        if (groupPin?.InternalPin == null)
            return false;

        // A GroupPin is occupied if any connection uses its InternalPin
        return allConnections.Any(conn =>
            conn.StartPin == groupPin.InternalPin ||
            conn.EndPin == groupPin.InternalPin);
    }

    /// <summary>
    /// Gets all unoccupied GroupPins for a ComponentGroup.
    /// </summary>
    /// <param name="group">The component group.</param>
    /// <param name="allConnections">All waveguide connections in the design.</param>
    /// <returns>List of unoccupied GroupPins.</returns>
    public static List<GroupPin> GetUnoccupiedPins(ComponentGroup group, IEnumerable<WaveguideConnection> allConnections)
    {
        if (group == null)
            return new List<GroupPin>();

        return group.ExternalPins
            .Where(pin => !IsOccupied(pin, allConnections))
            .ToList();
    }

    /// <summary>
    /// Calculates the absolute position of a GroupPin in world coordinates.
    /// GroupPins are positioned at the edge of the group boundary in the direction of the internal pin.
    /// </summary>
    /// <param name="groupPin">The group pin.</param>
    /// <param name="group">The parent component group.</param>
    /// <returns>Absolute (x, y) position in micrometers.</returns>
    public static (double X, double Y) GetAbsolutePosition(GroupPin groupPin, ComponentGroup group)
    {
        if (groupPin == null || group == null)
            return (0, 0);

        // GroupPins are positioned relative to the group's origin
        double absoluteX = group.PhysicalX + groupPin.RelativeX;
        double absoluteY = group.PhysicalY + groupPin.RelativeY;

        return (absoluteX, absoluteY);
    }

    /// <summary>
    /// Gets the absolute angle of a GroupPin in world-space.
    /// </summary>
    /// <param name="groupPin">The group pin.</param>
    /// <returns>Angle in degrees (0° = east, 90° = north, etc.).</returns>
    public static double GetAbsoluteAngle(GroupPin groupPin)
    {
        if (groupPin == null)
            return 0;

        // GroupPin angles are stored in world-space coordinates
        // Normalize to 0-360 range
        double angle = groupPin.AngleDegrees;
        while (angle < 0) angle += 360;
        while (angle >= 360) angle -= 360;
        return angle;
    }
}
