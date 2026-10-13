using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Taking crossings out again: when their wires went stale (records and chains), when a
/// component they touch is deleted, when a whole group is, or when the user cleans them up.
/// </summary>
public partial class CrossingInsertionService
{
    /// <summary>
    /// Dissolves every crossing whose net endpoints moved since placement, so the
    /// next routing + crossing pass re-evaluates them instead of forcing the nets
    /// through a leftover crossing forever.
    /// </summary>
    public void DissolveStaleRecords(WaveguideConnectionManager manager, WaveguideRouter router) =>
        _registry.DissolveStaleRecords(manager, router, (record, undo, added) =>
            ChangeRecorded?.Invoke(new CrossingChange(Array.Empty<Component>(), new[] { record.CrossingComponent }, added, undo)));

    /// <summary>
    /// Takes out every routing-placed crossing chain whose wire end moved, so the wire routes
    /// again and the insertion pass puts crossings back only where they are still needed.
    /// </summary>
    public void DissolveStaleChains(WaveguideConnectionManager manager, WaveguideRouter router) =>
        DissolveStaleChains(manager, router, piece => !PieceEndsAtItsPins(piece));

    /// <summary>
    /// Takes out every routing-placed crossing chain one of whose pieces
    /// <paramref name="isStale"/> rejects, and every such crossing that lost a piece. The
    /// stale wire merges pin to pin and routes again; the wires it crossed merge and keep
    /// their geometry, so untouched routes stay as they are. Crossings with a record are left
    /// to <see cref="DissolveStaleRecords"/>, which restores their originals.
    /// </summary>
    public void DissolveStaleChains(
        WaveguideConnectionManager manager, WaveguideRouter router, Func<WaveguideConnection, bool> isStale)
    {
        _dissolvedChains.Clear();
        var recorded = RecordedCrossings();
        var removed = ChainCrossingMaintenance.DissolveStaleChains(
            manager, router, isStale, c => ChainCrossingMaintenance.IsTemporaryCrossing(c) && !recorded.Contains(c),
            chain => { _dissolvedChains.Add(chain); ReportDissolved(chain); });
        NotifyRemoved(removed);
    }

    /// <summary>Chains taken out at the start of the current pass, for <see cref="RestoreDissolvedChains"/>.</summary>
    private readonly List<DissolvedChain> _dissolvedChains = new();

    /// <summary>
    /// For every wire taken out of its chain this pass that is still blocked: puts the old
    /// chain back with only its detached pieces re-routed, where that fits (see
    /// <see cref="DissolvedChainRepair"/>). Runs once, before the crossing search.
    /// </summary>
    private void RestoreDissolvedChains(
        WaveguideConnectionManager manager, WaveguideRouter router, CancellationToken cancellationToken)
    {
        foreach (var chain in _dissolvedChains)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!DissolvedChainRepair.TryRestore(chain, manager, router, cancellationToken)) continue;
            ReportRestored(chain, manager, router);
            foreach (var crossing in chain.Crossings)
                ComponentAdded?.Invoke(crossing);
        }
        _dissolvedChains.Clear();
    }

    /// <summary>
    /// Takes out the chains of the unrecorded routing-placed crossings that
    /// <paramref name="touches"/> reaches, before those wires are deleted or replaced, so an
    /// undo restores whole wires. Recorded crossings are left to the record-based paths,
    /// which restore their originals. Returns the crossings removed (already notified).
    /// </summary>
    public List<Component> DissolveChainsTouching(
        WaveguideConnectionManager manager, WaveguideRouter router, Func<WaveguideConnection, bool> touches)
    {
        var recorded = RecordedCrossings();
        var removed = ChainCrossingMaintenance.DissolveStaleChains(
            manager, router, touches, c => ChainCrossingMaintenance.IsTemporaryCrossing(c) && !recorded.Contains(c));
        NotifyRemoved(removed);
        return removed;
    }

    /// <summary>
    /// Takes out the chain of one piece of an unrecorded routing-placed crossing and returns
    /// its wire merged pin to pin (route dropped), or null when nothing was dissolved.
    /// </summary>
    public WaveguideConnection? DissolveChainOf(
        WaveguideConnection piece, WaveguideConnectionManager manager, WaveguideRouter router, out List<Component> removed)
    {
        var recorded = RecordedCrossings();
        removed = new List<Component>();
        var merged = ChainCrossingMaintenance.DissolveChainOf(
            piece, manager, router, c => ChainCrossingMaintenance.IsTemporaryCrossing(c) && !recorded.Contains(c), removed);
        NotifyRemoved(removed);
        return merged;
    }

    /// <summary>
    /// Removes every routing-placed crossing in the design: all wires through them merge pin
    /// to pin and route again. Crossings the user placed stay. Returns the crossings removed.
    /// </summary>
    public List<Component> DissolveAllInsertedCrossings(WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var removed = ChainCrossingMaintenance.DissolveAll(manager, router, ChainCrossingMaintenance.IsTemporaryCrossing);
        NotifyRemoved(removed);
        return removed;
    }

    private void NotifyRemoved(IEnumerable<Component> removed)
    {
        foreach (var crossing in removed)
        {
            _registry.Forget(crossing);
            ComponentRemoved?.Invoke(crossing);
        }
    }

    private HashSet<Component> RecordedCrossings() =>
        _registry.Records.Select(r => r.CrossingComponent).ToHashSet();

    /// <summary>
    /// True when the piece's route still ends at its pins. The tolerance matches the frozen
    /// endpoint check, so a piece whose outer pin moved reads as stale.
    /// </summary>
    private static bool PieceEndsAtItsPins(WaveguideConnection piece)
    {
        var path = piece.RoutedPath;
        if (path == null || path.Segments.Count == 0) return true;
        return Near(piece.StartPin, path.Segments[0].StartPoint) && Near(piece.EndPin, path.Segments[^1].EndPoint);
    }

    private static bool Near(PhysicalPin pin, (double X, double Y) point)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return Math.Abs(x - point.X) <= WaveguideConnection.FrozenEndpointToleranceMicrometers
               && Math.Abs(y - point.Y) <= WaveguideConnection.FrozenEndpointToleranceMicrometers;
    }

    /// <summary>
    /// Dissolves all crossings whose sub-connections touch the given component
    /// (e.g. because the component is being deleted). Originals not connected to
    /// that component are restored unsplit.
    /// </summary>
    public void DissolveForComponent(
        Component component, WaveguideConnectionManager manager, WaveguideRouter router) =>
        _registry.DissolveForComponent(component, manager, router);

    /// <summary>
    /// Dissolves all crossings touching a component or anything nested in it (a group being
    /// deleted). Originals not connected into the scope are restored unsplit.
    /// </summary>
    public void DissolveForScope(
        ComponentWireScope scope, WaveguideConnectionManager manager, WaveguideRouter router) =>
        _registry.DissolveForScope(scope, manager, router);
}
