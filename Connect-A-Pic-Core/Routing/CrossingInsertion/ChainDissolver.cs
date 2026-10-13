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
    /// <param name="dissolvable">
    /// Which crossings may be taken out; the chain ends at any other one (e.g. a crossing the
    /// user placed with the Cut tool). Null allows every crossing.
    /// </param>
    public static WaveguideConnection? TryDissolveChain(
        WaveguideConnection piece, WaveguideConnectionManager manager, WaveguideRouter router,
        out List<Component> dissolved, out Action undo, Func<Component, bool>? dissolvable = null) =>
        TryDissolveChain(piece, manager, router, out dissolved, out undo, out _, dissolvable);

    /// <inheritdoc cref="TryDissolveChain(WaveguideConnection, WaveguideConnectionManager, WaveguideRouter, out List{Component}, out Action, Func{Component, bool}?)"/>
    /// <param name="redo">Repeats the whole dissolution after <paramref name="undo"/>.</param>
    public static WaveguideConnection? TryDissolveChain(
        WaveguideConnection piece, WaveguideConnectionManager manager, WaveguideRouter router,
        out List<Component> dissolved, out Action undo, out Action redo, Func<Component, bool>? dissolvable = null)
    {
        dissolved = new List<Component>();
        var undos = new List<Action>();
        var redos = new List<Action>();
        undo = () => { for (int i = undos.Count - 1; i >= 0; i--) undos[i](); };
        redo = () => { foreach (var step in redos) step(); };
        var current = piece;
        for (int i = 0; i < MaxCrossingsPerChain; i++)
        {
            var startCrossing = CrossingAt(current.StartPin, dissolvable);
            var crossing = startCrossing ?? CrossingAt(current.EndPin, dissolvable);
            if (crossing == null) return current;
            var outerPin = startCrossing == null ? current.StartPin : current.EndPin;
            if (!TryDissolve(crossing, manager, router, out var merged, out var undoOne, out var redoOne))
            {
                undo();
                return null;
            }
            undos.Add(undoOne);
            redos.Add(redoOne);
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

    private static Component? CrossingAt(PhysicalPin pin, Func<Component, bool>? dissolvable) =>
        CrossingComponentCatalog.IsCrossing(pin.ParentComponent) && (dissolvable?.Invoke(pin.ParentComponent) ?? true)
            ? pin.ParentComponent
            : null;

    /// <summary>
    /// Merges the crossing's two through-pairs of pieces and removes it from the grid. False
    /// (nothing changed) when the crossing does not have the regular four docked pieces.
    /// </summary>
    internal static bool TryDissolve(Component crossing, WaveguideConnectionManager manager, WaveguideRouter router,
                                    out List<WaveguideConnection> merged, out Action undo) =>
        TryDissolve(crossing, manager, router, out merged, out undo, out _);

    /// <inheritdoc cref="TryDissolve(Component, WaveguideConnectionManager, WaveguideRouter, out List{WaveguideConnection}, out Action)"/>
    /// <param name="redo">Repeats the dissolution after <paramref name="undo"/> (a no-op on failure).</param>
    internal static bool TryDissolve(Component crossing, WaveguideConnectionManager manager, WaveguideRouter router,
                                    out List<WaveguideConnection> merged, out Action undo, out Action redo)
    {
        merged = new List<WaveguideConnection>();
        undo = () => { };
        redo = () => { };
        var pieces = new List<WaveguideConnection>();
        var pairs = ThroughPairs(crossing, manager);
        if (pairs.Count != 2) return false;

        foreach (var pair in pairs)
        {
            var connection = MergePair(pair);
            if (connection == null) return false;
            merged.Add(connection);
            pieces.Add(pair.In);
            pieces.Add(pair.Out);
        }
        (undo, redo) = Commit(crossing, pieces, merged, manager, router);
        return true;
    }

    /// <summary>
    /// Takes out a crossing that lost one of its pieces (a wire through it was deleted): a
    /// still complete through-pair merges into one wire with its geometry, every other piece
    /// docked at the crossing is dropped — it belonged to the deleted wire. False (nothing
    /// changed) when every port still has a piece.
    /// </summary>
    internal static bool TryDissolveBroken(Component crossing, WaveguideConnectionManager manager, WaveguideRouter router,
                                          out List<WaveguideConnection> merged, out List<WaveguideConnection> dropped)
    {
        merged = new List<WaveguideConnection>();
        dropped = new List<WaveguideConnection>();
        var docked = manager.Connections
            .Where(c => c.StartPin.ParentComponent == crossing || c.EndPin.ParentComponent == crossing).ToList();
        if (crossing.PhysicalPins.All(port => docked.Any(c => c.StartPin == port || c.EndPin == port)))
            return false;

        var kept = new List<WaveguideConnection>();
        foreach (var pair in ThroughPairs(crossing, manager))
        {
            var connection = MergePair(pair);
            if (connection == null) continue;
            merged.Add(connection);
            kept.Add(pair.In);
            kept.Add(pair.Out);
        }
        dropped = docked.Except(kept).ToList();
        Commit(crossing, docked, merged, manager, router);
        return true;
    }

    /// <summary>The crossing's straight-through pairs whose two pieces are both docked and routed.</summary>
    private static List<ThroughPair> ThroughPairs(Component crossing, WaveguideConnectionManager manager)
    {
        var pairs = new List<ThroughPair>();
        foreach (var entry in crossing.PhysicalPins)
        {
            var exit = CrossingComponentCatalog.StraightThroughExit(entry);
            var into = Docked(entry, manager, end: true);
            var outOf = exit == null ? null : Docked(exit, manager, end: false);
            if (into == null || outOf == null || into.RoutedPath == null || outOf.RoutedPath == null) continue;
            pairs.Add(new ThroughPair(into, outOf, entry, exit!));
        }
        return pairs;
    }

    /// <summary>One wire through the crossing: the piece in, the piece out, and their ports.</summary>
    private sealed record ThroughPair(WaveguideConnection In, WaveguideConnection Out, PhysicalPin Entry, PhysicalPin Exit);

    /// <summary>The pair as one connection keeping its exact geometry, or null when the pieces do not meet the ports.</summary>
    private static WaveguideConnection? MergePair(ThroughPair pair)
    {
        var path = MergedPath(pair.In.RoutedPath!, pair.Entry, pair.Exit, pair.Out.RoutedPath!);
        if (path == null) return null;
        var connection = CrossingPlacement.CreateSubConnection(pair.In, pair.In.StartPin, pair.Out.EndPin);
        connection.RestoreCachedPath(path);
        connection.IsRouteFrozen = pair.In.IsRouteFrozen && pair.Out.IsRouteFrozen;
        return connection;
    }

    /// <summary>
    /// Swaps the pieces for the merged wires and takes the crossing off the grid; returns the
    /// exact undo and a redo that repeats the swap with the merged wires' current routes.
    /// </summary>
    private static (Action Undo, Action Redo) Commit(
        Component crossing, List<WaveguideConnection> pieces, List<WaveguideConnection> merged,
        WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var grid = router.PathfindingGrid!;
        var added = merged.ToList();
        void Apply()
        {
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
                    if (m.IsPathValid && m.RoutedPath != null)
                        grid.AddWaveguideObstacle(m.Id, m.RoutedPath.Segments, manager.WaveguideWidthMicrometers);
                }
            }
        }
        Apply();
        return (() =>
        {
            lock (manager.SyncRoot)
            {
                foreach (var m in added)
                {
                    grid.RemoveWaveguideObstacle(m.Id);
                    manager.Connections.Remove(m);
                }
                router.AddComponentObstacle(crossing);
                foreach (var p in pieces.Where(p => p.RoutedPath != null))
                {
                    manager.Connections.Add(p);
                    grid.AddWaveguideObstacle(p.Id, p.RoutedPath!.Segments, manager.WaveguideWidthMicrometers);
                }
            }
        }, Apply);
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
