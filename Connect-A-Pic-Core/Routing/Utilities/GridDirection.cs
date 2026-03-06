using CAP_Core.Routing.Utilities;
namespace CAP_Core.Routing.Utilities;

/// <summary>
/// Represents a cardinal direction of travel on the pathfinding grid.
/// Using 4 directions (not 8) for cleaner manhattan-style routing.
/// </summary>
public enum GridDirection
{
    None = -1,
    East = 0,   // 0 degrees (pointing right)
    North = 1,  // 90 degrees (pointing up)
    West = 2,   // 180 degrees (pointing left)
    South = 3   // 270 degrees (pointing down)
}

/// <summary>
/// Extension methods for GridDirection.
/// </summary>
public static class GridDirectionExtensions
{
    private static readonly (int dx, int dy)[] Deltas =
    {
        (1, 0),   // East
        (0, 1),   // North
        (-1, 0),  // West
        (0, -1)   // South
    };

    /// <summary>
    /// Gets the grid delta (dx, dy) for moving in this direction.
    /// </summary>
    public static (int dx, int dy) GetDelta(this GridDirection dir)
    {
        if (dir == GridDirection.None)
            return (0, 0);
        return Deltas[(int)dir];
    }

    /// <summary>
    /// Gets the angle in degrees for this direction.
    /// </summary>
    public static double GetAngleDegrees(this GridDirection dir)
    {
        return dir switch
        {
            GridDirection.East => 0,
            GridDirection.North => 90,
            GridDirection.West => 180,
            GridDirection.South => 270,
            _ => 0
        };
    }

    /// <summary>
    /// Creates a GridDirection from an angle in degrees.
    /// Rounds to the nearest cardinal direction.
    /// </summary>
    public static GridDirection FromAngle(double degrees)
    {
        // Normalize to 0-360
        while (degrees < 0) degrees += 360;
        while (degrees >= 360) degrees -= 360;

        // Round to nearest 90 degrees
        if (degrees >= 315 || degrees < 45) return GridDirection.East;
        if (degrees >= 45 && degrees < 135) return GridDirection.North;
        if (degrees >= 135 && degrees < 225) return GridDirection.West;
        return GridDirection.South;
    }

    /// <summary>
    /// Gets the turn angle in degrees between two directions.
    /// Returns 0, 90, 180, or -90.
    /// </summary>
    public static double GetTurnAngle(GridDirection from, GridDirection to)
    {
        if (from == GridDirection.None || to == GridDirection.None)
            return 0;

        int diff = (int)to - (int)from;

        // Normalize to -2 to +2 range (represents -180 to +180 degrees in 90° steps)
        if (diff > 2) diff -= 4;
        if (diff < -2) diff += 4;

        return diff * 90.0;
    }

    /// <summary>
    /// Gets the opposite direction.
    /// </summary>
    public static GridDirection GetOpposite(this GridDirection dir)
    {
        return dir switch
        {
            GridDirection.East => GridDirection.West,
            GridDirection.West => GridDirection.East,
            GridDirection.North => GridDirection.South,
            GridDirection.South => GridDirection.North,
            _ => GridDirection.None
        };
    }

    /// <summary>
    /// Returns all cardinal directions.
    /// </summary>
    public static GridDirection[] GetAllDirections()
    {
        return new[] { GridDirection.East, GridDirection.North, GridDirection.West, GridDirection.South };
    }
}
