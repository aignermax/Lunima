using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Taking crossings out again: when their nets moved (stale records and chains), when a
/// component they touch is deleted, or when a whole group is.
/// </summary>
public partial class CrossingInsertionService
{
    /// <summary>
    /// Dissolves every crossing whose net endpoints moved since placement, so the
    /// next routing + crossing pass re-evaluates them instead of forcing the nets
    /// through a leftover crossing forever.
    /// </summary>
    public void DissolveStaleRecords(WaveguideConnectionManager manager, WaveguideRouter router) =>
        _registry.DissolveStaleRecords(manager, router);

    /// <summary>
    /// Takes out every crossing chain whose wire end moved, so the wire routes again and the
    /// insertion pass puts crossings back only where they are still needed.
    /// </summary>
    public void DissolveStaleChains(WaveguideConnectionManager manager, WaveguideRouter router)
    {
        foreach (var crossing in ChainCrossingMaintenance.DissolveStaleChains(manager, router, RecordedCrossings()))
            ComponentRemoved?.Invoke(crossing);
    }

    private HashSet<Component> RecordedCrossings() =>
        _registry.Records.Select(r => r.CrossingComponent).ToHashSet();

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
