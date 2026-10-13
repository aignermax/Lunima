using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Reports every change a routing pass makes to router-placed crossings, with its exact
/// undo (see <see cref="CrossingChange"/>), so the host can make undoing an edit restore the
/// crossings its re-route took out or placed.
/// </summary>
public partial class CrossingInsertionService
{
    /// <summary>
    /// Raised on the routing thread after each change a pass made to router-placed crossings.
    /// Commands that change crossings themselves (delete, clean-up) keep their own undo and are
    /// not reported.
    /// </summary>
    public Action<CrossingChange>? ChangeRecorded { get; set; }

    /// <summary>Connections currently in the design, for <see cref="Report"/>.</summary>
    private static HashSet<WaveguideConnection> Snapshot(WaveguideConnectionManager manager)
    {
        lock (manager.SyncRoot)
            return manager.Connections.ToHashSet();
    }

    /// <summary>Reports a change whose added connections are those not in <paramref name="before"/>.</summary>
    private void Report(
        WaveguideConnectionManager manager, HashSet<WaveguideConnection> before, Action? undo,
        IEnumerable<Component> added, IEnumerable<Component> removed)
    {
        if (undo == null || ChangeRecorded == null) return;
        List<WaveguideConnection> addedConnections;
        lock (manager.SyncRoot)
            addedConnections = manager.Connections.Where(c => !before.Contains(c)).ToList();
        ChangeRecorded(new CrossingChange(added.ToList(), removed.ToList(), addedConnections, undo));
    }

    /// <summary>Reports a chain the pass took out (its undo puts it back).</summary>
    private void ReportDissolved(DissolvedChain chain) =>
        ChangeRecorded?.Invoke(new CrossingChange(
            Array.Empty<Component>(), chain.Crossings, chain.Added, chain.Undo));

    /// <summary>Reports a dissolved chain the pass put back (its undo takes it out again).</summary>
    private void ReportRestored(DissolvedChain chain, WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var pieces = Snapshot(manager)
            .Where(c => chain.Crossings.Contains(c.StartPin.ParentComponent) || chain.Crossings.Contains(c.EndPin.ParentComponent))
            .ToList();
        ChangeRecorded?.Invoke(new CrossingChange(chain.Crossings, Array.Empty<Component>(), pieces, () =>
        {
            chain.Redo();
            router.PathfindingGrid?.RemoveWaveguideObstacle(chain.Merged.Id);
        }));
    }

    /// <summary>Undo of a detour crossing: the originals come back with their detours.</summary>
    private Action UndoDetourCrossing(CrossingRecord record, WaveguideConnectionManager manager, WaveguideRouter router) => () =>
    {
        lock (manager.SyncRoot)
            RollbackInsertion(record, manager, router);
        _registry.Forget(record.CrossingComponent);
    };
}
