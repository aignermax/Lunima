using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Mutable routing objects and crossing undo owned exclusively by one worker; never editor objects.</summary>
public sealed class RoutingWorkGraph
{
    private readonly List<Component> _components;
    private readonly List<CrossingChange> _crossingChanges = new();

    internal RoutingWorkGraph(IReadOnlyList<Component> components, WaveguideRouter router, WaveguideConnectionManager manager)
    {
        _components = components.ToList();
        Components = _components.AsReadOnly();
        Router = router;
        Manager = manager;
    }
    public IReadOnlyList<Component> Components { get; }
    public WaveguideRouter Router { get; }
    public WaveguideConnectionManager Manager { get; }
    // This journal intentionally stays inside the private graph. UI publication needs immutable topology values,
    // not these closures: each undo captures only private connections/components and their worker-owned grid.
    internal IReadOnlyList<CrossingChange> CrossingChanges => _crossingChanges;

    public void Route(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Manager.RecalculateAllTransmissions(cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void AddCrossing(Component component)
    {
        if (!_components.Contains(component)) _components.Add(component);
    }

    internal void RemoveCrossing(Component component) => _components.Remove(component);

    internal void RecordCrossingChange(CrossingChange change)
    {
        _crossingChanges.Add(change with
        {
            UndoAction = () =>
            {
                change.UndoAction();
                // Core topology undo restores connections/grid. The host owns its component collection.
                foreach (var added in change.Added) RemoveCrossing(added);
                foreach (var removed in change.Removed) AddCrossing(removed);
            }
        });
    }
}
