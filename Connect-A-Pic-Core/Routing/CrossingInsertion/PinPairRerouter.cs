using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Resolves a blocked wire sealed in by its pin neighbours — wires that leave pins a few
/// micrometres from its own (the two outputs of a copy gate, the two input arms of an MMI).
/// Whichever of them was routed first may have bent around the other pin and closed it in,
/// so the neighbours are lifted, the blocked wire is connected first, and the neighbours are
/// connected after it. Each wire goes through the crossing-chain inserter (plain route or
/// chain). The swap is kept only if every one of them connects; otherwise every change is
/// undone and the neighbours get their old routes back.
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
    /// Tries the pin-neighbour swap for <paramref name="blocked"/>. Returns the crossings placed
    /// (empty when none were needed), or null when the design was left unchanged.
    /// </summary>
    /// <param name="blocked">The blocked connection.</param>
    /// <param name="manager">The connection manager owning the design's connections.</param>
    /// <param name="router">The router whose grid holds the routed wires.</param>
    /// <param name="crossingFactory">Creates a fresh crossing component.</param>
    /// <param name="settings">Crossing geometry and search cost.</param>
    /// <param name="cancellationToken">Cancels the routing.</param>
    public IReadOnlyList<Component>? TryResolve(
        WaveguideConnection blocked, WaveguideConnectionManager manager, WaveguideRouter router,
        Func<Component?> crossingFactory, CrossingRouteSettings settings, CancellationToken cancellationToken)
    {
        var neighbours = PinNeighbours(blocked, manager);
        if (neighbours.Count == 0 || router.PathfindingGrid == null) return null;

        var lifted = neighbours.Select(n => (Connection: n, Path: n.RoutedPath!)).ToList();
        Lift(lifted, manager, router);
        var undos = new List<Action>();
        var placed = new List<Component>();
        bool connected = TryConnect(blocked, manager, router, crossingFactory, settings, cancellationToken, undos, placed);
        foreach (var (neighbour, _) in lifted)
        {
            if (!connected) break;
            ReturnAsPlaceholder(neighbour, manager);
            connected = TryConnect(neighbour, manager, router, crossingFactory, settings, cancellationToken, undos, placed);
        }
        if (connected) return placed;

        for (int i = undos.Count - 1; i >= 0; i--)
            undos[i]();
        Restore(lifted, blocked, manager, router);
        return null;
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
