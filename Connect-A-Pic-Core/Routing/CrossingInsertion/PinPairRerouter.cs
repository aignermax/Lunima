using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>The outcome of a kept pin-neighbour swap.</summary>
/// <param name="Placed">Crossings placed for the re-routed wires (the host adds them).</param>
/// <param name="Dissolved">Crossings taken out of neighbour chains (the host removes them).</param>
public sealed record PinPairResolution(IReadOnlyList<Component> Placed, IReadOnlyList<Component> Dissolved);

/// <summary>
/// Resolves a blocked wire sealed in by its pin neighbours — wires that leave pins a few
/// micrometres from its own (the two outputs of a copy gate, the two input arms of an MMI).
/// Whichever of them was routed first may have bent around the other pin and closed it in,
/// so the neighbours are lifted, the blocked wire is connected first, and the neighbours are
/// connected after it. Each wire goes through the crossing-chain inserter (plain route or
/// chain); while the blocked wire routes, the neighbours' pin leads stay reserved
/// (<see cref="PinLeadReservation"/>) so it cannot seal them in turn. A neighbour that is a
/// piece of a crossing chain has its whole chain dissolved
/// first (<see cref="ChainDissolver"/>), so it re-routes pin to pin instead of being tied to
/// its crossing ports. The swap is kept only if every wire connects; otherwise every change
/// is undone — chains included — and the neighbours get their old routes back.
/// </summary>
public sealed class PinPairRerouter
{
    /// <summary>Largest distance (µm) between pins whose wires count as pin neighbours.</summary>
    public const double NeighbourPinRadiusMicrometers = 12.0;

    private readonly CrossingChainInserter _inserter;

    /// <summary>Creates the rerouter on top of the chain inserter that connects each wire.</summary>
    /// <param name="inserter">Connects one wire (plain route or crossing chain) and can undo it.</param>
    public PinPairRerouter(CrossingChainInserter inserter) => _inserter = inserter;

    /// <summary>
    /// Undoes the most recent kept swap exactly — re-routed wires, placed crossings and
    /// dissolved chains alike. Null after a call that changed nothing.
    /// </summary>
    public Action? LastUndo { get; private set; }

    /// <summary>
    /// Tries the pin-neighbour swap for <paramref name="blocked"/>. Returns the crossings placed
    /// and dissolved, or null when the design was left unchanged.
    /// </summary>
    /// <param name="blocked">The blocked connection.</param>
    /// <param name="manager">The connection manager owning the design's connections.</param>
    /// <param name="router">The router whose grid holds the routed wires.</param>
    /// <param name="crossingFactory">Creates a fresh crossing component.</param>
    /// <param name="settings">Crossing geometry and search cost.</param>
    /// <param name="cancellationToken">Cancels the routing.</param>
    public PinPairResolution? TryResolve(
        WaveguideConnection blocked, WaveguideConnectionManager manager, WaveguideRouter router,
        Func<Component?> crossingFactory, CrossingRouteSettings settings, CancellationToken cancellationToken)
    {
        LastUndo = null;
        var candidates = PinNeighbours(blocked, manager);
        if (candidates.Count == 0 || router.PathfindingGrid == null) return null;

        var dissolveUndos = new List<Action>();
        var dissolved = new List<Component>();
        var neighbours = WholeWires(candidates, manager, router, dissolveUndos, dissolved);
        if (neighbours == null)
        {
            UndoAll(dissolveUndos);
            return null;
        }

        var lifted = neighbours.Select(n => (Connection: n, Path: n.RoutedPath!)).ToList();
        // First with the neighbours' leads reserved (the blocked wire must not seal them in
        // turn); if that fails, without — some layouts need the first wire to pass right there.
        foreach (bool reserveLeads in new[] { true, false })
        {
            var undos = new List<Action>();
            var placed = TrySwap(blocked, lifted, reserveLeads, manager, router, crossingFactory, settings, cancellationToken, undos);
            if (placed == null) continue;
            LastUndo = () =>
            {
                UndoAll(undos);
                Restore(lifted, blocked, manager, router);
                UndoAll(dissolveUndos);
            };
            return new PinPairResolution(placed, dissolved);
        }
        UndoAll(dissolveUndos);
        return null;
    }

    /// <summary>
    /// One attempt of the swap: lift the neighbours, connect the blocked wire, then each
    /// neighbour. Returns the placed crossings, or null after undoing the attempt completely.
    /// </summary>
    private List<Component>? TrySwap(
        WaveguideConnection blocked, List<(WaveguideConnection Connection, RoutedPath Path)> lifted, bool reserveLeads,
        WaveguideConnectionManager manager, WaveguideRouter router, Func<Component?> crossingFactory,
        CrossingRouteSettings settings, CancellationToken cancellationToken, List<Action> undos)
    {
        Lift(lifted, manager, router);
        var placed = new List<Component>();
        var leads = reserveLeads
            ? PinLeadReservation.Reserve(router.PathfindingGrid!, lifted.Select(l => l.Connection), blocked,
                (a, b) => IsNear(a, b), manager.WaveguideWidthMicrometers)
            : null;
        bool connected = TryConnect(blocked, manager, router, crossingFactory, settings, cancellationToken, undos, placed);
        leads?.Release();
        foreach (var (neighbour, _) in lifted)
        {
            if (!connected) break;
            ReturnAsPlaceholder(neighbour, manager);
            connected = TryConnect(neighbour, manager, router, crossingFactory, settings, cancellationToken, undos, placed);
        }
        if (connected) return placed;

        UndoAll(undos);
        Restore(lifted, blocked, manager, router);
        return null;
    }

    /// <summary>
    /// The neighbours as whole wires: a chain piece's chain is dissolved so the wire runs pin to
    /// pin. Null when a chain cannot be dissolved (the undos made so far are in <paramref name="undos"/>).
    /// </summary>
    private static List<WaveguideConnection>? WholeWires(
        List<WaveguideConnection> candidates, WaveguideConnectionManager manager, WaveguideRouter router,
        List<Action> undos, List<Component> dissolved)
    {
        var whole = new List<WaveguideConnection>();
        foreach (var candidate in candidates)
        {
            // A neighbour merged away by an earlier dissolution of the same chain is covered already.
            if (!manager.Connections.Contains(candidate)) continue;
            if (!IsCrossingPiece(candidate))
            {
                whole.Add(candidate);
                continue;
            }
            var merged = ChainDissolver.TryDissolveChain(candidate, manager, router, out var gone, out var undo);
            undos.Add(undo);
            if (merged == null) return null;
            dissolved.AddRange(gone);
            whole.Add(merged);
        }
        return whole.Where(manager.Connections.Contains).Distinct().ToList();
    }

    private static void UndoAll(List<Action> undos)
    {
        for (int i = undos.Count - 1; i >= 0; i--)
            undos[i]();
    }

    /// <summary>
    /// Routed wires with an endpoint next to one of the blocked wire's pins that may be
    /// re-routed: unfrozen wires, and the frozen pieces of a crossing chain (re-routing a
    /// piece only changes how it reaches its crossing port). A wire the user froze between
    /// two components stays where it is.
    /// </summary>
    private static List<WaveguideConnection> PinNeighbours(WaveguideConnection blocked, WaveguideConnectionManager manager)
    {
        var pins = new[] { blocked.StartPin, blocked.EndPin }.Select(p => p.GetAbsolutePosition()).ToArray();
        return manager.Connections
            .Where(c => c != blocked && c.RoutedPath != null && !c.IsBlockedFallback && (!c.IsRouteFrozen || IsCrossingPiece(c)))
            .Where(c => new[] { c.StartPin, c.EndPin }.Any(p => pins.Any(q => IsNear(p.GetAbsolutePosition(), q))))
            .ToList();
    }

    private static bool IsCrossingPiece(WaveguideConnection connection) =>
        CrossingComponentCatalog.IsCrossing(connection.StartPin.ParentComponent)
        || CrossingComponentCatalog.IsCrossing(connection.EndPin.ParentComponent);

    private static bool IsNear((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy <= NeighbourPinRadiusMicrometers * NeighbourPinRadiusMicrometers;
    }

    private bool TryConnect(
        WaveguideConnection wire, WaveguideConnectionManager manager, WaveguideRouter router,
        Func<Component?> crossingFactory, CrossingRouteSettings settings, CancellationToken cancellationToken,
        List<Action> undos, List<Component> placed)
    {
        var crossings = _inserter.TryInsert(wire, manager, router, crossingFactory, settings, cancellationToken);
        if (crossings == null || _inserter.LastUndo == null) return false;
        undos.Add(_inserter.LastUndo);
        placed.AddRange(crossings);
        return true;
    }

    /// <summary>Takes the neighbours out of the design and the grid while the blocked wire routes.</summary>
    private static void Lift(List<(WaveguideConnection Connection, RoutedPath Path)> lifted,
                             WaveguideConnectionManager manager, WaveguideRouter router)
    {
        lock (manager.SyncRoot)
        {
            foreach (var (neighbour, _) in lifted)
            {
                router.PathfindingGrid!.RemoveWaveguideObstacle(neighbour.Id);
                manager.Connections.Remove(neighbour);
            }
        }
    }

    /// <summary>
    /// Puts a lifted neighbour back as a placeholder: a blocked straight line that is no
    /// geometry, so neither the grid nor the crossing guard sees its old route.
    /// </summary>
    private static void ReturnAsPlaceholder(WaveguideConnection neighbour, WaveguideConnectionManager manager)
    {
        var (sx, sy) = neighbour.StartPin.GetAbsolutePosition();
        var (ex, ey) = neighbour.EndPin.GetAbsolutePosition();
        var placeholder = new RoutedPath { IsBlockedFallback = true };
        placeholder.Segments.Add(new StraightSegment(sx, sy, ex, ey, neighbour.StartPin.AngleDegrees));
        lock (manager.SyncRoot)
        {
            neighbour.RestoreCachedPath(placeholder);
            manager.Connections.Add(neighbour);
        }
    }

    /// <summary>After an undo: the neighbours get their old routes back, the blocked wire its placeholder role.</summary>
    private static void Restore(List<(WaveguideConnection Connection, RoutedPath Path)> lifted,
                                WaveguideConnection blocked, WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var grid = router.PathfindingGrid!;
        lock (manager.SyncRoot)
        {
            foreach (var (neighbour, path) in lifted)
            {
                neighbour.RestoreCachedPath(path);
                if (!manager.Connections.Contains(neighbour))
                    manager.Connections.Add(neighbour);
                grid.AddWaveguideObstacle(neighbour.Id, path.Segments, manager.WaveguideWidthMicrometers);
            }
            // The caller keeps blocked fallbacks out of the grid while it connects them.
            grid.RemoveWaveguideObstacle(blocked.Id);
        }
    }
}
