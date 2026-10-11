using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace UnitTests.Integration;

/// <summary>
/// Undoes overlaps an earlier group bake left behind: a path frozen inside a plain group (a
/// word cell's wiring, not a gate's own waveguides) that cuts through a top-level wire is
/// turned back into a blocked placeholder, so the design reports it honestly and the fenced
/// group bake can route it again properly.
/// </summary>
internal static class HiddenOverlapRepair
{
    /// <summary>Demotes every such path and returns how many were demoted.</summary>
    public static int Demote(DesignCanvasViewModel canvas)
    {
        var topLevelWires = canvas.ConnectionManager.Connections
            .Where(c => !c.IsBlockedFallback && c.RoutedPath != null).Select(c => c.RoutedPath!).ToList();
        int demoted = 0;
        foreach (var group in canvas.Components.Select(c => c.Component).OfType<ComponentGroup>())
        {
            if (group.TruthTablePinAssignment != null) continue;
            foreach (var path in group.InternalPaths.ToList())
            {
                if (path.Path.IsBlockedFallback || path.StartPin == null || path.EndPin == null) continue;
                if (!topLevelWires.Any(wire => PathIntersectionDetector.Crosses(path.Path, wire))) continue;
                path.Path = Placeholder(path.StartPin, path.EndPin);
                demoted++;
            }
        }
        return demoted;
    }

    private static RoutedPath Placeholder(PhysicalPin start, PhysicalPin end)
    {
        var (sx, sy) = start.GetAbsolutePosition();
        var (ex, ey) = end.GetAbsolutePosition();
        var path = new RoutedPath { IsBlockedFallback = true };
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, start.GetAbsoluteAngle()));
        return path;
    }
}
