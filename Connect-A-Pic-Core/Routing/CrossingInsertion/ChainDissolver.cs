using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Takes a crossing out of a chain: each of the two wires through it gets its two docked
/// pieces merged back into one connection — piece, the straight run through the crossing,
/// piece — so both keep exactly the geometry they had, and the crossing component leaves
/// the routing grid. Used to make a chained wire re-routable as a whole; every dissolution
/// comes with an exact undo. The caller tells the host about removed crossings only once it
/// commits.
/// </summary>
internal static class ChainDissolver
{
    /// <summary>Most crossings one wire's chain may be dissolved through.</summary>
    private const int MaxCrossingsPerChain = 64;

    /// <summary>Largest gap (µm) between a piece's end and its crossing port.</summary>
    private const double PortToleranceMicrometers = 0.5;

    /// <summary>
    /// Dissolves every crossing along the chain of <paramref name="piece"/> and returns the
    /// merged connection that runs pin to pin, or null (with nothing changed) when one of the
    /// crossings does not have the regular four docked pieces.
    /// </summary>
    /// <param name="piece">A chain piece (one of its ends docks onto a crossing port).</param>
    /// <param name="manager">The connection manager owning the design's connections.</param>
    /// <param name="router">The router whose grid holds the routed wires.</param>
    /// <param name="dissolved">The crossings taken out, in order.</param>
    /// <param name="undo">Restores the chain exactly (set even on failure, a no-op then).</param>
    public static WaveguideConnection? TryDissolveChain(
        WaveguideConnection piece, WaveguideConnectionManager manager, WaveguideRouter router,
        out List<Component> dissolved, out Action undo)
    {
        dissolved = new List<Component>();
        var undos = new List<Action>();
        undo = () => { for (int i = undos.Count - 1; i >= 0; i--) undos[i](); };
        var current = piece;
        for (int i = 0; i < MaxCrossingsPerChain; i++)
        {
            var crossing = CrossingAt(current.StartPin) ?? CrossingAt(current.EndPin);
            if (crossing == null) return current;
            var outerPin = CrossingAt(current.StartPin) == null ? current.StartPin : current.EndPin;
            if (!TryDissolve(crossing, manager, router, out var merged, out var undoOne))
            {
                undo();
                return null;
            }
            undos.Add(undoOne);
            dissolved.Add(crossing);
            var next = merged.FirstOrDefault(m => m.StartPin == outerPin || m.EndPin == outerPin);
            if (next == null)
            {
                undo();
                return null;
            }
            current = next;
        }
        undo();
        return null;
    }

    private static Component? CrossingAt(PhysicalPin pin) =>
        CrossingComponentCatalog.IsCrossing(pin.ParentComponent) ? pin.ParentComponent : null;

    /// <summary>Merges the crossing's two through-pairs of pieces and removes it from the grid.</summary>
    private static bool TryDissolve(Component crossing, WaveguideConnectionManager manager, WaveguideRouter router,
                                    out List<WaveguideConnection> merged, out Action undo)
    {
        merged = new List<WaveguideConnection>();
        undo = () => { };
        var pieces = new List<WaveguideConnection>();
        var pairs = new List<(WaveguideConnection In, WaveguideConnection Out, PhysicalPin Entry, PhysicalPin Exit)>();
        foreach (var entry in crossing.PhysicalPins)
        {
            var exit = CrossingComponentCatalog.StraightThroughExit(entry);
            var into = Docked(entry, manager, end: true);
            var outOf = exit == null ? null : Docked(exit, manager, end: false);
            if (into == null || outOf == null || into.RoutedPath == null || outOf.RoutedPath == null) continue;
            pairs.Add((into, outOf, entry, exit!));
        }
        if (pairs.Count != 2) return false;

        foreach (var (into, outOf, entry, exit) in pairs)
        {
            var path = MergedPath(into.RoutedPath!, entry, exit, outOf.RoutedPath!);
            if (path == null) return false;
            var connection = CrossingPlacement.CreateSubConnection(into, into.StartPin, outOf.EndPin);
            connection.RestoreCachedPath(path);
            connection.IsRouteFrozen = into.IsRouteFrozen && outOf.IsRouteFrozen;
            merged.Add(connection);
            pieces.Add(into);
            pieces.Add(outOf);
        }

        var grid = router.PathfindingGrid!;
        var added = merged.ToList();
        lock (manager.SyncRoot)
        {
            foreach (var p in pieces)
            {
                grid.RemoveWaveguideObstacle(p.Id);
                manager.Connections.Remove(p);
            }
            router.RemoveComponentObstacle(crossing);
            foreach (var m in added)
            {
                manager.Connections.Add(m);
                grid.AddWaveguideObstacle(m.Id, m.RoutedPath!.Segments, manager.WaveguideWidthMicrometers);
            }
        }
        undo = () =>
        {
            lock (manager.SyncRoot)
            {
                foreach (var m in added)
                {
                    grid.RemoveWaveguideObstacle(m.Id);
                    manager.Connections.Remove(m);
                }
                router.AddComponentObstacle(crossing);
                foreach (var p in pieces)
                {
                    manager.Connections.Add(p);
                    grid.AddWaveguideObstacle(p.Id, p.RoutedPath!.Segments, manager.WaveguideWidthMicrometers);
                }
            }
        };
        return true;
    }

    /// <summary>The single connection that ends (or starts) at the port, or null.</summary>
    private static WaveguideConnection? Docked(PhysicalPin port, WaveguideConnectionManager manager, bool end)
    {
        var docked = manager.Connections.Where(c => c.StartPin == port || c.EndPin == port).ToList();
        if (docked.Count != 1) return null;
        var connection = docked[0];
        return (end ? connection.EndPin : connection.StartPin) == port ? connection : null;
    }

    /// <summary>The piece into the crossing, the straight between its ports, and the piece out of it.</summary>
    private static RoutedPath? MergedPath(RoutedPath into, PhysicalPin entry, PhysicalPin exit, RoutedPath outOf)
    {
        if (into.Segments.Count == 0 || outOf.Segments.Count == 0) return null;
        var entryPoint = entry.GetAbsolutePosition();
        var exitPoint = exit.GetAbsolutePosition();
        if (!Near(into.Segments[^1].EndPoint, entryPoint) || !Near(outOf.Segments[0].StartPoint, exitPoint)) return null;
        var path = new RoutedPath();
        path.Segments.AddRange(into.Segments);
        path.Segments.Add(new StraightSegment(entryPoint.x, entryPoint.y, exitPoint.x, exitPoint.y, into.Segments[^1].EndAngleDegrees));
        path.Segments.AddRange(outOf.Segments);
        return path;
    }

    private static bool Near((double X, double Y) a, (double x, double y) b) =>
        Math.Abs(a.X - b.x) <= PortToleranceMicrometers && Math.Abs(a.Y - b.y) <= PortToleranceMicrometers;
}
