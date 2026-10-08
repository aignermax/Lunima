using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Connects a blocked wire through crossings: routes it crossing-aware, then places one
/// crossing component per planned crossing, splits each crossed wire there (the halves
/// dock onto the crossing's opposite ports) and lays the new wire as a chain of legs from
/// crossing to crossing. Every piece keeps the routed geometry — nothing is re-routed — and
/// is frozen. The design changes only if every piece fits; otherwise nothing changes.
/// Chain crossings are permanent design parts, like crossings placed with the Cut tool.
/// </summary>
public sealed class CrossingChainInserter
{
    /// <summary>
    /// Optional diagnostics: called with the reason whenever a blocked wire stays blocked
    /// (no crossing route, or a crossing that does not dock onto the routed geometry).
    /// </summary>
    public Action<string>? Rejected { get; set; }

    /// <summary>
    /// Undoes the most recent successful <see cref="TryInsert"/>: the chain's pieces and
    /// crossings leave the design and the wire returns as it was. Null after a failed call.
    /// </summary>
    public Action? LastUndo { get; private set; }

    private sealed class Plan
    {
        public List<Component> Crossings { get; } = new();
        public List<WaveguideConnection> Removed { get; } = new();
        public List<(WaveguideConnection Connection, RoutedPath Path)> Added { get; } = new();
    }

    /// <summary>
    /// Tries to connect <paramref name="blocked"/> through crossings. Returns the placed
    /// crossing components (the host adds them to its model) — empty when the search found a
    /// way without any crossing — or null when the wire stays as it was.
    /// </summary>
    /// <param name="blocked">The blocked connection to route through crossings.</param>
    /// <param name="manager">The connection manager owning the design's connections.</param>
    /// <param name="router">The router (its grid holds the routed wires).</param>
    /// <param name="crossingFactory">Creates a fresh crossing component, or null when none is available.</param>
    /// <param name="settings">Crossing geometry and search cost.</param>
    /// <param name="cancellationToken">Cancels the routing.</param>
    public IReadOnlyList<Component>? TryInsert(
        WaveguideConnection blocked, WaveguideConnectionManager manager, WaveguideRouter router,
        Func<Component?> crossingFactory, CrossingRouteSettings settings, CancellationToken cancellationToken)
    {
        LastUndo = null;
        var grid = router.PathfindingGrid;
        if (grid == null || blocked.StartPin == null || blocked.EndPin == null) return null;

        // A blocked fallback is a placeholder line, not geometry — the caller keeps every
        // fallback out of the grid while it connects blocked wires (see the service).
        var (route, planned) = RouteWithCrossings(blocked, router, settings, cancellationToken);
        if (route == null)
            return Reject("no route");
        if (planned.Count == 0)
        {
            if (!PlainRouteApplier.TryApply(blocked, route, manager, router, out var undoPlain))
                return Reject("the crossing-free route crosses another wire");
            LastUndo = undoPlain;
            return Array.Empty<Component>();
        }
        var plan = BuildPlan(blocked, route, planned, manager, crossingFactory, settings, grid.CellSizeMicrometers);
        if (plan == null)
            return null;
        if (ChainPieceCrossingGuard.CrossesAnotherWire(plan.Added, plan.Removed, manager))
            return Reject("a piece crosses another wire outside its crossings");
        if (!Apply(plan, manager, router))
            return Reject("a docked piece is invalid");
        LastUndo = () => { lock (manager.SyncRoot) Rollback(plan, manager, router); };
        return plan.Crossings;
    }

    private static (RoutedPath? Route, IReadOnlyList<PlannedCrossing> Planned) RouteWithCrossings(
        WaveguideConnection blocked, WaveguideRouter router, CrossingRouteSettings settings, CancellationToken cancellationToken)
    {
        var previous = router.CrossingRouting;
        router.CrossingRouting = settings;
        try
        {
            var route = router.Route(blocked.StartPin, blocked.EndPin, cancellationToken);
            bool usable = route.IsValid && !route.IsBlockedFallback && !route.IsInvalidGeometry;
            return usable ? (route, router.LastPlannedCrossings) : (null, Array.Empty<PlannedCrossing>());
        }
        finally
        {
            router.CrossingRouting = previous;
        }
    }

    private Plan? BuildPlan(
        WaveguideConnection blocked, RoutedPath route, IReadOnlyList<PlannedCrossing> planned,
        WaveguideConnectionManager manager, Func<Component?> crossingFactory, CrossingRouteSettings settings,
        double cellSizeMicrometers)
    {
        var plan = new Plan();
        var pieces = new Dictionary<Guid, List<(WaveguideConnection Connection, RoutedPath Path)>>();
        double half = settings.CrossingEdgeMicrometers / 2;
        var legStart = blocked.StartPin;
        var legPath = route;

        foreach (var gridCrossing in planned)
        {
            if (!TryLocateOnGeometry(gridCrossing, route, manager, pieces, cellSizeMicrometers, out var crossingPoint))
                return RejectPlan("crossing not at a right angle on the routed geometry");
            var center = (crossingPoint.CenterX, crossingPoint.CenterY);
            var crossing = crossingFactory();
            if (crossing == null) return RejectPlan("no crossing component available");
            crossing.PhysicalX = center.Item1 - crossing.WidthMicrometers / 2;
            crossing.PhysicalY = center.Item2 - crossing.HeightMicrometers / 2;
            plan.Crossings.Add(crossing);

            if (!TrySplitCrossedWire(crossingPoint, crossing, half, manager, pieces, plan)) return null;
            if (!RoutedPathCutter.TryCutAround(legPath, center, half, out var legBefore, out var legAfter, out var direction)
                || !CrossingDocking.TryResolvePorts(crossing, direction, out var entry, out var exit)
                || !CrossingDocking.EndsAt(legBefore, entry))
                return RejectPlan("the new route has no straight run through a crossing");
            plan.Added.Add((CrossingPlacement.CreateSubConnection(blocked, legStart, entry), legBefore));
            legStart = exit;
            legPath = legAfter;
        }
        plan.Added.Add((CrossingPlacement.CreateSubConnection(blocked, legStart, blocked.EndPin), legPath));
        plan.Removed.Add(blocked);
        foreach (var crossed in pieces.Values.SelectMany(list => list))
            plan.Added.Add(crossed);
        return plan;
    }

    /// <summary>
    /// Moves a grid-planned crossing onto the exact intersection of the new route and the
    /// crossed wire (the grid quantizes it to cell centres; the cuts must be on the geometry).
    /// </summary>
    private bool TryLocateOnGeometry(
        PlannedCrossing gridCrossing, RoutedPath route, WaveguideConnectionManager manager,
        Dictionary<Guid, List<(WaveguideConnection Connection, RoutedPath Path)>> pieces,
        double cellSizeMicrometers, out PlannedCrossing exact)
    {
        exact = gridCrossing;
        var near = (gridCrossing.CenterX, gridCrossing.CenterY);
        var crossedPaths = pieces.TryGetValue(gridCrossing.CrossedConnection, out var list)
            ? list.Select(p => p.Path)
            : manager.Connections.Where(c => c.Id == gridCrossing.CrossedConnection && c.RoutedPath != null).Select(c => c.RoutedPath!);
        foreach (var crossedPath in crossedPaths)
        {
            if (!RoutedPathCutter.TryFindRightAngleCrossing(route, crossedPath, near, cellSizeMicrometers, out var center))
                continue;
            exact = gridCrossing with { CenterX = center.X, CenterY = center.Y };
            return true;
        }
        return false;
    }

    /// <summary>Splits the piece of the crossed wire that carries the crossing point into two docked halves.</summary>
    private bool TrySplitCrossedWire(
        PlannedCrossing crossingPoint, Component crossing, double half, WaveguideConnectionManager manager,
        Dictionary<Guid, List<(WaveguideConnection Connection, RoutedPath Path)>> pieces, Plan plan)
    {
        var center = (crossingPoint.CenterX, crossingPoint.CenterY);
        if (!pieces.TryGetValue(crossingPoint.CrossedConnection, out var list))
        {
            var original = manager.Connections.FirstOrDefault(c => c.Id == crossingPoint.CrossedConnection);
            if (original?.RoutedPath == null || original.IsBlockedFallback) return RejectSplit("crossed wire is gone or blocked");
            pieces[crossingPoint.CrossedConnection] = list = new() { (original, original.RoutedPath) };
            plan.Removed.Add(original);
        }
        int index = list.FindIndex(p => RoutedPathCutter.Carries(p.Path, center, half));
        if (index < 0) return RejectSplit("no piece of the crossed wire carries the crossing");
        var (piece, path) = list[index];
        if (!RoutedPathCutter.TryCutAround(path, center, half, out var before, out var after, out var direction)
            || !CrossingDocking.TryResolvePorts(crossing, direction, out var entry, out var exit)
            || !CrossingDocking.EndsAt(before, entry))
            return RejectSplit("the crossed wire has no straight run through the crossing");
        list[index] = (CrossingPlacement.CreateSubConnection(piece, piece.StartPin, entry), before);
        list.Insert(index + 1, (CrossingPlacement.CreateSubConnection(piece, exit, piece.EndPin), after));
        return true;
    }

    private IReadOnlyList<Component>? Reject(string reason)
    {
        Rejected?.Invoke(reason);
        return null;
    }

    private Plan? RejectPlan(string reason)
    {
        Rejected?.Invoke(reason);
        return null;
    }

    private bool RejectSplit(string reason)
    {
        Rejected?.Invoke(reason);
        return false;
    }

    /// <summary>Swaps the removed connections for the added, frozen pieces; undoes it if any piece is invalid.</summary>
    private static bool Apply(Plan plan, WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var grid = router.PathfindingGrid!;
        lock (manager.SyncRoot)
        {
            foreach (var removed in plan.Removed)
            {
                grid.RemoveWaveguideObstacle(removed.Id);
                manager.Connections.Remove(removed);
            }
            foreach (var crossing in plan.Crossings)
                router.AddComponentObstacle(crossing);
            foreach (var (connection, path) in plan.Added)
            {
                connection.RestoreCachedPath(path);
                connection.IsRouteFrozen = true;
                manager.Connections.Add(connection);
                if (connection.IsPathValid && connection.RoutedPath != null)
                    grid.AddWaveguideObstacle(connection.Id, connection.RoutedPath.Segments, manager.WaveguideWidthMicrometers);
            }
            if (plan.Added.All(a => a.Connection.IsPathValid && !a.Connection.IsBlockedFallback))
                return true;
            Rollback(plan, manager, router);
            return false;
        }
    }

    private static void Rollback(Plan plan, WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var grid = router.PathfindingGrid!;
        foreach (var (connection, _) in plan.Added)
        {
            grid.RemoveWaveguideObstacle(connection.Id);
            manager.Connections.Remove(connection);
        }
        foreach (var crossing in plan.Crossings)
            router.RemoveComponentObstacle(crossing);
        foreach (var removed in plan.Removed)
        {
            manager.Connections.Add(removed);
            RestoreObstacle(removed, manager, router);
        }
    }

    private static void RestoreObstacle(WaveguideConnection connection, WaveguideConnectionManager manager, WaveguideRouter router)
    {
        if (connection.IsPathValid && connection.RoutedPath != null)
            router.PathfindingGrid?.AddWaveguideObstacle(connection.Id, connection.RoutedPath.Segments, manager.WaveguideWidthMicrometers);
    }
}
