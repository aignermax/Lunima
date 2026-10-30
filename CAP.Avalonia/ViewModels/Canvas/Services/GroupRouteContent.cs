using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>Publishes edited internal wires into their document group, independently of navigation or saving.</summary>
internal sealed class GroupRouteContent : IDisposable
{
    private readonly ObservableCollection<WaveguideConnectionViewModel> _connections;
    private readonly Func<bool> _isProjecting;
    private readonly Action _requestSynchronization;
    private readonly HashSet<WaveguideConnectionViewModel> _subscribed = new();
    private readonly HashSet<WaveguideConnectionViewModel> _changed = new();
    private readonly Dictionary<WaveguideConnectionViewModel, FrozenWaveguidePath> _paths = new();
    private ComponentGroup? _scope;
    private bool _topologyChanged;

    internal GroupRouteContent(ObservableCollection<WaveguideConnectionViewModel> connections,
        Func<bool> isProjecting, Action requestSynchronization)
    {
        _connections = connections;
        _isProjecting = isProjecting;
        _requestSynchronization = requestSynchronization;
        connections.CollectionChanged += ConnectionsChanged;
        foreach (var wire in connections) Attach(wire);
    }

    internal void Project(ComponentGroup? scope)
    {
        _scope = scope;
        _changed.Clear();
        _paths.Clear();
        _topologyChanged = false;
    }

    internal void ProjectionCompleted()
    {
        if (_scope == null) return;
        var stored = _scope.InternalPaths.ToDictionary(path => path.PathId);
        foreach (var wire in _connections)
            if (stored.TryGetValue(wire.Connection.Id, out var path)) _paths.Add(wire, path);
    }

    private void ConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs change)
    {
        if (change.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var wire in _subscribed.ToArray()) Detach(wire);
            foreach (var wire in _connections) Attach(wire);
        }
        else
        {
            if (change.OldItems != null)
                foreach (WaveguideConnectionViewModel wire in change.OldItems) Detach(wire);
            if (change.NewItems != null)
                foreach (WaveguideConnectionViewModel wire in change.NewItems) Attach(wire);
        }
        if (_scope == null || _isProjecting()) return;
        _topologyChanged = true;
        _requestSynchronization();
    }

    private void Attach(WaveguideConnectionViewModel wire)
    {
        if (_subscribed.Add(wire)) wire.PropertyChanged += PathChanged;
    }

    private void Detach(WaveguideConnectionViewModel wire)
    {
        if (_subscribed.Remove(wire)) wire.PropertyChanged -= PathChanged;
        _changed.Remove(wire);
    }

    private void PathChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (_scope == null || _isProjecting() || change.PropertyName != nameof(WaveguideConnectionViewModel.PathLength)) return;
        _changed.Add((WaveguideConnectionViewModel)sender!);
        _requestSynchronization();
    }

    internal bool Synchronize()
    {
        if (_scope == null || (!_topologyChanged && _changed.Count == 0)) return false;
        if (_topologyChanged)
        {
            var present = _connections.ToHashSet();
            foreach (var removed in _paths.Keys.Where(wire => !present.Contains(wire)).ToArray()) _paths.Remove(removed);
            foreach (var wire in _connections)
                if (!_paths.ContainsKey(wire))
                {
                    _paths.Add(wire, new FrozenWaveguidePath());
                    _changed.Add(wire);
                }
            var pinless = _scope.InternalPaths.Where(path => path.StartPin == null || path.EndPin == null).ToArray();
            _scope.InternalPaths.Clear();
            _scope.InternalPaths.AddRange(_connections.Select(wire => _paths[wire]));
            _scope.InternalPaths.AddRange(pinless);
        }
        foreach (var wire in _changed)
        {
            var connection = wire.Connection;
            var path = _paths[wire];
            path.PathId = connection.Id;
            path.StartPin = connection.StartPin;
            path.EndPin = connection.EndPin;
            path.Path = connection.RoutedPath?.DeepCopy() ?? new RoutedPath();
            path.CaptureSettingsFrom(connection);
        }
        _changed.Clear();
        _topologyChanged = false;
        _scope.RefreshInternalPaths();
        return true;
    }

    public void Dispose()
    {
        _connections.CollectionChanged -= ConnectionsChanged;
        foreach (var wire in _subscribed.ToArray()) Detach(wire);
        _paths.Clear();
    }
}
