using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Keeps the crossings routing placed temporary: they carry
/// <see cref="Component.IsInsertedCrossing"/> and their pieces are ordinary auto routes. A
/// crossing whose wire went stale (an outer end moved, a piece needs a new route, a piece was
/// deleted) is taken out structurally — both wires through it merge back with their exact
/// geometry and only the stale wire routes again — and deleting such a crossing merges the
/// wires through it instead of deleting them. Crossings the user placed (library, Cut tool)
/// are never flagged and therefore never touched.
/// </summary>
public static class ChainCrossingMaintenance
{
    /// <summary>Ports of a crossing — each docks one piece of a chain crossing.</summary>
    private const int CrossingPortCount = 4;

    /// <summary>Upper bound on dissolution sweeps; every sweep removes at least one crossing.</summary>
    private const int MaxSweeps = 256;

    /// <summary>
    /// Designs saved before chain crossings carried the flag: a crossing with all four ports
    /// docked by frozen pieces was placed by crossing-aware routing (a Cut-tool crossing docks
    /// the two halves of one unfrozen wire). Flags such crossings and unfreezes their pieces.
    /// Returns the number of crossings adopted.
    /// </summary>
    public static int AdoptLegacyChainCrossings(IEnumerable<Component> components, WaveguideConnectionManager manager)
    {
        var connections = manager.Connections.ToList();
        // Decide on every crossing before unfreezing anything: neighbours in a chain share a piece.
        var adopted = components
            .Where(c => CrossingComponentCatalog.IsCrossing(c) && !c.IsInsertedCrossing)
            .Select(c => (Crossing: c, Pieces: DockedPieces(c, connections)))
            .Where(x => x.Pieces.Count == CrossingPortCount && x.Pieces.All(p => p.IsRouteFrozen))
            .ToList();
        foreach (var (crossing, pieces) in adopted)
        {
            crossing.IsInsertedCrossing = true;
            foreach (var piece in pieces)
                piece.IsRouteFrozen = false;
        }
        return adopted.Count;
    }

    /// <summary>True for a crossing routing placed — the only kind maintenance may take out.</summary>
    public static bool IsTemporaryCrossing(Component? component) =>
        component is { IsInsertedCrossing: true } && CrossingComponentCatalog.IsCrossing(component);

    /// <summary>True when one end of <paramref name="connection"/> docks onto a temporary crossing.</summary>
    public static bool DocksOnTemporaryCrossing(WaveguideConnection connection) =>
        IsTemporaryCrossing(connection.StartPin.ParentComponent) || IsTemporaryCrossing(connection.EndPin.ParentComponent);

    /// <summary>
    /// Takes out every chain of a stale piece: the stale wire's pieces merge back into one
    /// connection whose route is dropped so the next pass routes it anew; the wires crossed
    /// along the way merge too and keep their geometry. Crossings that lost a piece are taken
    /// out as well. Returns the crossings removed — the caller tells its host.
    /// </summary>
    /// <param name="manager">The connection manager owning the design's connections.</param>
    /// <param name="router">The router whose grid holds the routed wires.</param>
    /// <param name="isStale">Whether a piece's route can no longer be kept as it is.</param>
    /// <param name="dissolvable">Which crossings may be taken out.</param>
    /// <param name="onDissolved">Receives every dissolved chain with its exact undo and redo.</param>
    public static List<Component> DissolveStaleChains(
        WaveguideConnectionManager manager, WaveguideRouter router,
        Func<WaveguideConnection, bool> isStale, Func<Component, bool> dissolvable,
        Action<DissolvedChain>? onDissolved = null)
    {
        var removed = new List<Component>();
        if (router.PathfindingGrid == null) return removed;
        foreach (var piece in manager.Connections.ToList())
        {
            if (manager.Connections.Contains(piece) && DocksOn(piece, dissolvable) && isStale(piece))
                DissolveChainOf(piece, manager, router, dissolvable, removed, onDissolved);
        }
        removed.AddRange(DissolveBrokenCrossings(manager, router, dissolvable));
        return removed;
    }

    /// <summary>
    /// Takes out the chain of one piece: its wire merges pin to pin with its route dropped
    /// (returned), the wires it crossed merge and keep their geometry. Null when the piece
    /// docks onto no dissolvable crossing or the chain is irregular (nothing changed).
    /// </summary>
    /// <param name="removed">Receives the crossings taken out.</param>
    /// <param name="onDissolved">Receives the dissolved chain with its exact undo and redo.</param>
    public static WaveguideConnection? DissolveChainOf(
        WaveguideConnection piece, WaveguideConnectionManager manager, WaveguideRouter router,
        Func<Component, bool> dissolvable, List<Component> removed, Action<DissolvedChain>? onDissolved = null)
    {
        if (router.PathfindingGrid == null || !DocksOn(piece, dissolvable)) return null;
        var before = manager.Connections.ToHashSet();
        var merged = ChainDissolver.TryDissolveChain(piece, manager, router, out var dissolved, out var undo, out var redo, dissolvable);
        if (merged == null) return null;
        Reroute(merged, router);
        removed.AddRange(dissolved);
        if (onDissolved != null)
            onDissolved(new DissolvedChain(merged, dissolved, manager.Connections.Where(c => !before.Contains(c)).ToList(), undo, redo));
        return merged;
    }

    /// <summary>
    /// Takes out every crossing <paramref name="dissolvable"/> selects: all wires through them
    /// merge pin to pin and their routes are dropped, so the next pass routes them anew.
    /// Returns the crossings removed.
    /// </summary>
    public static List<Component> DissolveAll(
        WaveguideConnectionManager manager, WaveguideRouter router, Func<Component, bool> dissolvable)
    {
        var removed = new List<Component>();
        if (router.PathfindingGrid == null) return removed;
        var before = manager.Connections.ToHashSet();
        var stuck = new HashSet<Component>();
        for (int sweep = 0; sweep < MaxSweeps; sweep++)
        {
            var piece = manager.Connections.FirstOrDefault(c => DocksOn(c, dissolvable) && !stuck.Contains(Crossing(c, dissolvable)));
            if (piece == null) break;
            var merged = ChainDissolver.TryDissolveChain(piece, manager, router, out var dissolved, out _, dissolvable);
            if (merged != null)
            {
                removed.AddRange(dissolved);
                continue;
            }
            // An irregular crossing (a piece missing) is taken out on its own.
            var crossing = Crossing(piece, dissolvable);
            if (ChainDissolver.TryDissolveBroken(crossing, manager, router, out _, out var dropped))
            {
                RemoveDropped(dropped, manager, router);
                removed.Add(crossing);
            }
            else
            {
                stuck.Add(crossing);
            }
        }
        foreach (var connection in manager.Connections.Where(c => !before.Contains(c)).ToList())
            Reroute(connection, router);
        return removed;
    }

    /// <summary>
    /// Deleting a chain crossing keeps both wires through it: they merge pin to pin and are
    /// routed again (crossing each other without a crossing component is no valid layout).
    /// False when <paramref name="crossing"/> is no chain crossing or lacks its four pieces.
    /// </summary>
    public static bool DissolveForDeletion(
        Component crossing, WaveguideConnectionManager manager, WaveguideRouter router,
        IReadOnlySet<Component> recordedCrossings)
    {
        if (router.PathfindingGrid == null || !IsTemporaryCrossing(crossing) || recordedCrossings.Contains(crossing)) return false;
        if (!ChainDissolver.TryDissolve(crossing, manager, router, out var merged, out _)) return false;
        foreach (var connection in merged)
            Reroute(connection, router);
        return true;
    }

    /// <summary>
    /// Takes out every crossing that lost a piece: its complete through-wire merges and keeps
    /// its geometry, the remaining pieces of the deleted wire go. Dropping such a piece can
    /// break the next crossing of its chain, so this sweeps until none is left.
    /// </summary>
    private static List<Component> DissolveBrokenCrossings(
        WaveguideConnectionManager manager, WaveguideRouter router, Func<Component, bool> dissolvable)
    {
        var removed = new List<Component>();
        for (int sweep = 0; sweep < MaxSweeps; sweep++)
        {
            var crossings = manager.Connections
                .SelectMany(c => new[] { c.StartPin.ParentComponent, c.EndPin.ParentComponent })
                .Where(c => CrossingComponentCatalog.IsCrossing(c) && dissolvable(c))
                .Distinct().ToList();
            Component? broken = null;
            foreach (var crossing in crossings)
            {
                if (!ChainDissolver.TryDissolveBroken(crossing, manager, router, out _, out var dropped)) continue;
                RemoveDropped(dropped, manager, router);
                broken = crossing;
                break;
            }
            if (broken == null) break;
            removed.Add(broken);
        }
        return removed;
    }

    private static void RemoveDropped(List<WaveguideConnection> dropped, WaveguideConnectionManager manager, WaveguideRouter router)
    {
        lock (manager.SyncRoot)
        {
            foreach (var piece in dropped)
            {
                router.PathfindingGrid?.RemoveWaveguideObstacle(piece.Id);
                manager.Connections.Remove(piece);
            }
        }
    }

    /// <summary>Drops a merged wire's route (and its grid footprint) so the next pass routes it anew.</summary>
    private static void Reroute(WaveguideConnection connection, WaveguideRouter router)
    {
        connection.IsRouteFrozen = false;
        router.PathfindingGrid?.RemoveWaveguideObstacle(connection.Id);
        connection.InvalidateRoute();
    }

    private static bool DocksOn(WaveguideConnection piece, Func<Component, bool> dissolvable) =>
        IsDissolvable(piece.StartPin.ParentComponent, dissolvable) || IsDissolvable(piece.EndPin.ParentComponent, dissolvable);

    private static Component Crossing(WaveguideConnection piece, Func<Component, bool> dissolvable) =>
        IsDissolvable(piece.StartPin.ParentComponent, dissolvable) ? piece.StartPin.ParentComponent : piece.EndPin.ParentComponent;

    private static bool IsDissolvable(Component? component, Func<Component, bool> dissolvable) =>
        CrossingComponentCatalog.IsCrossing(component) && dissolvable(component!);

    private static List<WaveguideConnection> DockedPieces(Component crossing, IReadOnlyList<WaveguideConnection> connections) =>
        connections.Where(c => c.StartPin.ParentComponent == crossing || c.EndPin.ParentComponent == crossing).ToList();
}
