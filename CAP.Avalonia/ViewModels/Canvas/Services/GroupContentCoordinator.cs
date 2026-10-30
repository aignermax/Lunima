using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>Keeps edited group topology in the document while treating navigation as projection only.</summary>
internal sealed class GroupContentCoordinator : IDisposable
{
    private readonly ObservableCollection<ComponentViewModel> _components;
    private readonly GroupRouteContent _routes;
    private ComponentGroup? _scope;
    private int _projectionDepth;
    private int _editDepth;
    private bool _dirty;
    internal bool IsProjecting => _projectionDepth > 0;

    public GroupContentCoordinator(ObservableCollection<ComponentViewModel> components,
        ObservableCollection<WaveguideConnectionViewModel> connections)
    {
        _components = components;
        _components.CollectionChanged += OnComponentsChanged;
        _routes = new GroupRouteContent(connections, () => IsProjecting, RequestSynchronization);
    }

    public IDisposable Project(ComponentGroup? scope)
    {
        Synchronize();
        _scope = scope;
        _projectionDepth++;
        _routes.Project(scope);
        return new Projection(this);
    }

    public void BeginEdit() => _editDepth++;
    public void EndEdit()
    {
        if (_editDepth == 0) throw new InvalidOperationException("No group edit batch is active.");
        _editDepth--;
        if (_editDepth == 0) Synchronize();
    }

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_projectionDepth > 0 || _scope == null) return;
        _dirty = true;
        if (_editDepth == 0) Synchronize();
    }

    public void Synchronize()
    {
        if (_projectionDepth > 0) return;
        var childrenChanged = _dirty;
        if (_dirty)
        {
            _dirty = false;
            _scope?.ReplaceChildren(_components.Select(view => view.Component).ToArray());
        }
        // Legacy model operations can move children directly. Reconcile derived
        // bounds at a scope/command boundary without recapturing unchanged wires.
        if (!_routes.Synchronize() && !childrenChanged) _scope?.UpdateGroupBounds();
    }

    private void RequestSynchronization()
    {
        if (_editDepth == 0) Synchronize();
    }

    public void Dispose()
    {
        _components.CollectionChanged -= OnComponentsChanged;
        _routes.Dispose();
    }

    private sealed class Projection(GroupContentCoordinator owner) : IDisposable
    {
        private GroupContentCoordinator? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current != null && --current._projectionDepth == 0) current._routes.ProjectionCompleted();
        }
    }
}
