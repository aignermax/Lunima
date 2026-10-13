using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Crossings routing placed are temporary whether or not adaptive crossing insertion is on:
/// the bookkeeping service stays attached as <see cref="CrossingMaintenance"/> so every pass
/// takes out the crossings whose wires went stale, deletions never leave a crossing behind,
/// and the user can clean all of them up. Only placing new crossings needs
/// <see cref="CrossingInsertion"/>.
/// </summary>
public partial class WaveguideConnectionManager
{
    /// <summary>
    /// Crossing bookkeeping that stays attached while adaptive insertion is off: it dissolves
    /// stale routing-placed crossings and tells its host about removed ones, but never places
    /// any. Null keeps routing-placed crossings untouched (no host to tell).
    /// </summary>
    public CrossingInsertionService? CrossingMaintenance { get; set; }

    /// <summary>The service that dissolves crossings: the insertion service when on, the maintenance one otherwise.</summary>
    public CrossingInsertionService? CrossingService => CrossingInsertion ?? CrossingMaintenance;

    /// <summary>
    /// Takes out every routing-placed crossing whose wire went stale before routing starts:
    /// a piece that the incremental pass would re-route (its outer end moved, its route is
    /// gone, blocked, or runs through a component) dissolves its chain, and a crossing that
    /// lost a piece is taken out too. Only the stale wire routes again.
    /// </summary>
    private void DissolveStaleCrossings()
    {
        _rerouteAfterDissolution = new HashSet<WaveguideConnection>();
        var service = CrossingService;
        if (service == null) return;
        var before = SnapshotConnections().ToHashSet();
        service.DissolveStaleChains(this, _router, piece => IsStalePiece(piece) && !IsQueuedForRerouteInPlace(piece));
        service.DissolveStaleRecords(this, _router);
        _rerouteAfterDissolution = SnapshotConnections().Where(c => !before.Contains(c)).ToHashSet();
    }

    /// <summary>
    /// The wires the current pass took out of their crossings. Their re-route never starts
    /// the ordering cascade: they needed a crossing, which the crossing pass places again
    /// where adaptive insertion is on — re-routing the whole design would only churn it.
    /// </summary>
    private HashSet<WaveguideConnection> _rerouteAfterDissolution = new();

    /// <summary>
    /// A piece of a routing-placed crossing whose route still ends at both pins: it stays as
    /// it is (no re-route, not part of the ordering cascade) until it goes stale, at which
    /// point its whole chain is taken out.
    /// </summary>
    private static bool IsSettledPiece(WaveguideConnection connection) =>
        ChainCrossingMaintenance.DocksOnTemporaryCrossing(connection) && !IsStalePiece(connection);

    /// <summary>
    /// A piece whose route cannot stay: gone, blocked, or detached from one of its pins (a
    /// component it ends at moved). Grid collisions are deliberately not a criterion: a piece
    /// docks into a crossing port and often ends between the narrow pins of a gate input, so
    /// the rasterized grid reports settled pieces as touching component cells.
    /// </summary>
    private static bool IsStalePiece(WaveguideConnection piece)
    {
        var path = piece.RoutedPath;
        if (path == null || !piece.IsPathValid || path.IsBlockedFallback || path.Segments.Count == 0)
            return true;
        var (startX, startY) = piece.StartPin.GetAbsolutePosition();
        var (endX, endY) = piece.EndPin.GetAbsolutePosition();
        return Distance(path.Segments[0].StartPoint.X, path.Segments[0].StartPoint.Y, startX, startY) > EndpointToleranceMicrometers
               || Distance(path.Segments[^1].EndPoint.X, path.Segments[^1].EndPoint.Y, endX, endY) > EndpointToleranceMicrometers;
    }

    /// <summary>
    /// Wires a user edit asked to route again where they dock (see <see cref="RerouteInPlace"/>).
    /// Guarded by its own lock: commands add on the UI thread, the routing pass reads and prunes.
    /// </summary>
    private readonly HashSet<WaveguideConnection> _reroutesInPlace = new();

    /// <summary>
    /// Drops the route of <paramref name="connection"/> so the next pass routes it again
    /// between its current pins, WITHOUT taking out the routing-placed crossing chain it docks
    /// onto: the route-less piece would otherwise read as stale and dissolve that chain. Used
    /// for deliberate user edits (moving a crossing, deleting a user-placed one) whose wires
    /// still end exactly at the crossings they dock onto.
    /// </summary>
    public void RerouteInPlace(WaveguideConnection connection)
    {
        lock (_reroutesInPlace)
            _reroutesInPlace.Add(connection);
        _router.PathfindingGrid?.RemoveWaveguideObstacle(connection.Id);
        connection.IsRouteFrozen = false;
        connection.InvalidateRoute();
    }

    /// <summary>
    /// Swaps <paramref name="removed"/> for <paramref name="added"/> in one step — a wire for
    /// the pieces a cut split it into, or back. When every added wire carries a valid route the
    /// occupied geometry is unchanged, so blocked wires keep their verdict and the next pass
    /// re-routes nothing; otherwise the swap counts as a changed wire set.
    /// </summary>
    public void SwapConnections(IReadOnlyCollection<WaveguideConnection> removed, IReadOnlyCollection<WaveguideConnection> added)
    {
        if (added.Any(c => !c.IsPathValid))
            NoteWireSetChanged();
        var grid = _router.PathfindingGrid;
        lock (_connectionsSync)
        {
            foreach (var connection in removed)
            {
                grid?.RemoveWaveguideObstacle(connection.Id);
                Connections.Remove(connection);
            }
            foreach (var connection in added.Where(c => !Connections.Contains(c)))
            {
                Connections.Add(connection);
                if (connection.IsPathValid && connection.RoutedPath != null)
                    grid?.AddWaveguideObstacle(connection.Id, connection.RoutedPath.Segments, WaveguideWidthMicrometers);
            }
        }
    }

    private bool IsQueuedForRerouteInPlace(WaveguideConnection connection)
    {
        lock (_reroutesInPlace)
            return _reroutesInPlace.Contains(connection);
    }

    /// <summary>
    /// Forgets queued wires that got a route (or left the design). A wire whose pass was cut
    /// short keeps its entry, so the next pass still routes it in place.
    /// </summary>
    private void PruneReroutesInPlace()
    {
        var present = SnapshotConnections().ToHashSet();
        lock (_reroutesInPlace)
            _reroutesInPlace.RemoveWhere(c => c.RoutedPath != null || !present.Contains(c));
    }

    /// <summary>
    /// Takes out the chains of routing-placed crossings that the selected wires dock onto, so
    /// they run pin to pin before a command deletes or replaces them. Returns the crossings
    /// removed (the host was told); empty when no crossing service is attached.
    /// </summary>
    public List<Component> DissolveInsertedChainsTouching(Func<WaveguideConnection, bool> touches) =>
        CrossingService?.DissolveChainsTouching(this, _router, touches) ?? new List<Component>();

    /// <summary>
    /// Takes out the chain of one piece of a routing-placed crossing and returns its wire,
    /// merged pin to pin with its route dropped; null when nothing was dissolved.
    /// </summary>
    public WaveguideConnection? DissolveInsertedChainOf(WaveguideConnection piece, out List<Component> removed)
    {
        removed = new List<Component>();
        return CrossingService?.DissolveChainOf(piece, this, _router, out removed);
    }

    /// <summary>
    /// Puts the connections back into <paramref name="order"/>; connections not in it follow
    /// in their current order. Routing order decides which wire gets the space first, so an
    /// exact undo restores it along with the wires.
    /// </summary>
    public void RestoreConnectionOrder(IReadOnlyList<WaveguideConnection> order)
    {
        List<WaveguideConnection> present;
        lock (_connectionsSync)
            present = order.Where(Connections.Contains).ToList();
        ReorderConnections(present);
    }

    /// <summary>
    /// Removes every routing-placed crossing: the wires through them merge pin to pin and route
    /// again on the next pass (with new crossings only where adaptive insertion is on and one
    /// is needed). User-placed crossings stay. Returns the crossings removed.
    /// </summary>
    public List<Component> RemoveAllInsertedCrossings()
    {
        var removed = CrossingService?.DissolveAllInsertedCrossings(this, _router)
                      ?? ChainCrossingMaintenance.DissolveAll(this, _router, ChainCrossingMaintenance.IsTemporaryCrossing);
        return removed;
    }
}
