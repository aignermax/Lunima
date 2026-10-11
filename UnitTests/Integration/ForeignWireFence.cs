using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;

namespace UnitTests.Integration;

/// <summary>
/// Keeps routes inside a group off the design's top-level wires. The group bakes route a
/// group's interior on a scratch router that only holds the group's own wiring, while
/// top-level wires run through the group's area to the pins of its nested gates — without
/// a fence, an internal route would cut straight through them (no crossing component, so
/// the export overlaps). The fence marks the top-level wires as uncrossable cells (like a
/// frozen group path) and checks finished internal routes against their exact geometry.
/// </summary>
internal static class ForeignWireFence
{
    /// <summary>Uncrossable cell state — the one frozen group paths use; pin corridors never clear it.</summary>
    private const byte Uncrossable = 3;

    /// <summary>Marks every top-level wire that passes the grid's area as uncrossable.</summary>
    public static void Mark(PathfindingGrid grid, IEnumerable<RoutedPath> topLevelWires, double waveguideWidthMicrometers)
    {
        double radius = waveguideWidthMicrometers / 2 + grid.CellSizeMicrometers / 2;
        foreach (var wire in topLevelWires)
            foreach (var (x, y) in Samples(wire, grid.CellSizeMicrometers / 2))
                MarkDisc(grid, x, y, radius);
    }

    /// <summary>True when one of <paramref name="routes"/> properly crosses one of the top-level wires.</summary>
    public static bool AnyCrossing(IEnumerable<RoutedPath> routes, IReadOnlyList<RoutedPath> topLevelWires) =>
        routes.Any(route => topLevelWires.Any(wire => PathIntersectionDetector.Crosses(route, wire)));

    private static IEnumerable<(double X, double Y)> Samples(RoutedPath path, double step)
    {
        foreach (var segment in path.Segments)
        {
            if (segment is BendSegment bend)
            {
                foreach (var point in ArcSampling.SamplePoints(bend, step))
                    yield return point;
                continue;
            }
            var (sx, sy) = segment.StartPoint;
            var (ex, ey) = segment.EndPoint;
            double length = Math.Max(Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy)), 1e-9);
            for (double t = 0; t <= length; t += step)
                yield return (sx + (ex - sx) * t / length, sy + (ey - sy) * t / length);
            yield return (ex, ey);
        }
    }

    private static void MarkDisc(PathfindingGrid grid, double x, double y, double radius)
    {
        var (gx1, gy1) = grid.PhysicalToGrid(x - radius, y - radius);
        var (gx2, gy2) = grid.PhysicalToGrid(x + radius, y + radius);
        for (int gx = gx1; gx <= gx2; gx++)
        for (int gy = gy1; gy <= gy2; gy++)
        {
            var (cx, cy) = grid.GridToPhysical(gx, gy);
            if ((cx - x) * (cx - x) + (cy - y) * (cy - y) <= radius * radius)
                grid.SetCellState(gx, gy, Uncrossable);
        }
    }
}
