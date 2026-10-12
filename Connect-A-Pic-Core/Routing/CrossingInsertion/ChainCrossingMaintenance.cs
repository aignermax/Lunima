using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Keeps the crossings that crossing-aware routing placed (chains of crossings along one
/// wire) as adaptive as single inserted crossings: they carry
/// <see cref="Component.IsInsertedCrossing"/>, their pieces are ordinary auto routes, a chain
/// whose wire end moved is taken out so the wire routes again from scratch (the insertion pass
/// puts crossings back only where they are still needed), and deleting such a crossing merges
/// the wires through it instead of deleting them. Crossings tracked by a
/// <see cref="CrossingRecord"/> and crossings the user placed with the Cut tool are left alone.
/// </summary>
public static class ChainCrossingMaintenance
{
    /// <summary>Ports of a crossing — each docks one piece of a chain crossing.</summary>
    private const int CrossingPortCount = 4;

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

    /// <summary>
    /// Takes out every chain whose wire end left its pin (a component moved): the wire's pieces
    /// merge back into one connection, whose route is dropped so the next pass routes it anew;
    /// the wires crossed along the way merge too and keep their geometry. Returns the crossings
    /// removed — the caller tells its host.
    /// </summary>
    public static List<Component> DissolveStaleChains(
        WaveguideConnectionManager manager, WaveguideRouter router, IReadOnlySet<Component> recordedCrossings)
    {
        var removed = new List<Component>();
        if (router.PathfindingGrid == null) return removed;
        bool Dissolvable(Component crossing) => IsChainCrossing(crossing, recordedCrossings);

        foreach (var piece in manager.Connections.ToList())
        {
            if (!manager.Connections.Contains(piece) || !HasMovedOuterEnd(piece, Dissolvable)) continue;
            var merged = ChainDissolver.TryDissolveChain(piece, manager, router, out var dissolved, out _, Dissolvable);
            if (merged == null) continue;
            Reroute(merged, router);
            removed.AddRange(dissolved);
        }
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
        if (router.PathfindingGrid == null || !IsChainCrossing(crossing, recordedCrossings)) return false;
        if (!ChainDissolver.TryDissolve(crossing, manager, router, out var merged, out _)) return false;
        foreach (var connection in merged)
            Reroute(connection, router);
        return true;
    }

    /// <summary>Drops a merged wire's route (and its grid footprint) so the next pass routes it anew.</summary>
    private static void Reroute(WaveguideConnection connection, WaveguideRouter router)
    {
        connection.IsRouteFrozen = false;
        router.PathfindingGrid?.RemoveWaveguideObstacle(connection.Id);
        connection.InvalidateRoute();
    }

    private static bool IsChainCrossing(Component crossing, IReadOnlySet<Component> recordedCrossings) =>
        crossing.IsInsertedCrossing && CrossingComponentCatalog.IsCrossing(crossing) && !recordedCrossings.Contains(crossing);

    /// <summary>
    /// True when <paramref name="piece"/> docks onto a chain crossing at one end and its other
    /// end — a component pin — no longer sits where the route ends.
    /// </summary>
    private static bool HasMovedOuterEnd(WaveguideConnection piece, Func<Component, bool> dissolvable)
    {
        var path = piece.RoutedPath;
        if (path == null || path.Segments.Count == 0) return false;
        bool startDocked = IsDockedAt(piece.StartPin, dissolvable);
        bool endDocked = IsDockedAt(piece.EndPin, dissolvable);
        if (startDocked == endDocked) return false;
        var (pin, point) = startDocked
            ? (piece.EndPin, path.Segments[^1].EndPoint)
            : (piece.StartPin, path.Segments[0].StartPoint);
        var (x, y) = pin.GetAbsolutePosition();
        return Math.Abs(x - point.X) > WaveguideConnection.FrozenEndpointToleranceMicrometers
               || Math.Abs(y - point.Y) > WaveguideConnection.FrozenEndpointToleranceMicrometers;
    }

    private static bool IsDockedAt(PhysicalPin pin, Func<Component, bool> dissolvable) =>
        pin.ParentComponent is { } parent && CrossingComponentCatalog.IsCrossing(parent) && dissolvable(parent);

    private static List<WaveguideConnection> DockedPieces(Component crossing, IReadOnlyList<WaveguideConnection> connections) =>
        connections.Where(c => c.StartPin.ParentComponent == crossing || c.EndPin.ParentComponent == crossing).ToList();
}
