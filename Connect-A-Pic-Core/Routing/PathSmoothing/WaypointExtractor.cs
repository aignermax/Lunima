using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.Utilities;

namespace CAP_Core.Routing.PathSmoothing;

/// <summary>
/// Extracts waypoints (corners) from A* grid paths.
/// Converts dense grid paths to sparse corner points where direction changes.
/// </summary>
public class WaypointExtractor
{
    /// <summary>
    /// Extracts corner points where the path changes direction.
    /// </summary>
    public List<(int X, int Y, GridDirection Direction)> ExtractCorners(List<AStarNode> gridPath)
    {
        var corners = new List<(int X, int Y, GridDirection Direction)>();

        if (gridPath == null || gridPath.Count == 0)
            return corners;

        Console.WriteLine($"[WaypointExtractor] Extracting corners from {gridPath.Count} grid nodes");

        // Always add start
        corners.Add((gridPath[0].X, gridPath[0].Y, gridPath[0].Direction));

        // Add direction changes
        for (int i = 1; i < gridPath.Count - 1; i++)
        {
            if (gridPath[i].Direction != gridPath[i - 1].Direction)
            {
                corners.Add((gridPath[i].X, gridPath[i].Y, gridPath[i].Direction));
            }
        }

        // Always add end
        if (gridPath.Count > 1)
        {
            var last = gridPath[^1];
            corners.Add((last.X, last.Y, last.Direction));
        }

        Console.WriteLine($"[WaypointExtractor] Extracted {corners.Count} corners");
        for (int i = 0; i < Math.Min(corners.Count, 10); i++)
        {
            Console.WriteLine($"[WaypointExtractor]   Corner {i}: ({corners[i].X},{corners[i].Y}) dir={corners[i].Direction}");
        }

        return corners;
    }

    /// <summary>
    /// Finds the index of the last corner that involves a direction change.
    /// Used to determine where to start terminal approach.
    /// </summary>
    public int FindLastTurningCorner(List<(int X, int Y, GridDirection Direction)> corners, double initialAngle)
    {
        if (corners.Count <= 1)
            return 0;

        for (int i = corners.Count - 1; i >= 1; i--)
        {
            double angle = AngleUtilities.DirectionToAngle(corners[i].Direction);
            if (!AngleUtilities.IsAngleClose(angle, initialAngle))
            {
                Console.WriteLine($"[WaypointExtractor] FindLastTurningCorner: Found turn at index {i} (angle={angle}° vs initial={initialAngle}°)");
                return i;
            }

            initialAngle = angle;
        }

        Console.WriteLine($"[WaypointExtractor] FindLastTurningCorner: No turn found, returning {corners.Count - 1}");
        return corners.Count - 1;
    }
}
