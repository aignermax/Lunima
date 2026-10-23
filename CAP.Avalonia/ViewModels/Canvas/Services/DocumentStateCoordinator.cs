using System.Collections.Specialized;
using System.ComponentModel;
using CAP.Avalonia.ViewModels.Simulation;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>Translates editor mutations into revision changes, excluding selection and navigation projection.</summary>
internal sealed class DocumentStateCoordinator : IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Dictionary<ComponentViewModel, LaserConfig?> _components = new();
    private readonly HashSet<WaveguideConnectionViewModel> _connections = new();

    public DocumentStateCoordinator(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
        canvas.Components.CollectionChanged += ComponentsChanged;
        canvas.Connections.CollectionChanged += ConnectionsChanged;
        canvas.CanvasFrozenPaths.CollectionChanged += GeometryChanged;
        foreach (var component in canvas.Components) Attach(component);
        foreach (var wire in canvas.Connections) Attach(wire);
    }

    private void ComponentsChanged(object? sender, NotifyCollectionChangedEventArgs change)
    {
        if (change.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var component in _components.Keys.ToArray()) Detach(component);
            foreach (var component in _canvas.Components) Attach(component);
        }
        else
        {
            if (change.OldItems != null)
                foreach (ComponentViewModel component in change.OldItems) Detach(component);
            if (change.NewItems != null)
                foreach (ComponentViewModel component in change.NewItems) Attach(component);
        }
        Changed();
    }

    private void ConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs change)
    {
        if (change.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var wire in _connections.ToArray()) Detach(wire);
            foreach (var wire in _canvas.Connections) Attach(wire);
        }
        else
        {
            if (change.OldItems != null)
                foreach (WaveguideConnectionViewModel wire in change.OldItems) Detach(wire);
            if (change.NewItems != null)
                foreach (WaveguideConnectionViewModel wire in change.NewItems) Attach(wire);
        }
        Changed();
    }

    private void Attach(ComponentViewModel component)
    {
        if (!_components.TryAdd(component, component.LaserConfig)) return;
        component.PropertyChanged += ComponentChanged;
        if (component.LaserConfig != null) component.LaserConfig.PropertyChanged += LaserChanged;
    }

    private void Detach(ComponentViewModel component)
    {
        if (!_components.Remove(component, out var laser)) return;
        component.PropertyChanged -= ComponentChanged;
        if (laser != null) laser.PropertyChanged -= LaserChanged;
    }

    private void Attach(WaveguideConnectionViewModel wire)
    {
        if (_connections.Add(wire)) wire.PropertyChanged += ConnectionChanged;
    }

    private void Detach(WaveguideConnectionViewModel wire)
    {
        if (_connections.Remove(wire)) wire.PropertyChanged -= ConnectionChanged;
    }

    private void ComponentChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(ComponentViewModel.IsSelected)) return;
        if (change.PropertyName == nameof(ComponentViewModel.LaserConfig) && sender is ComponentViewModel component)
        {
            Detach(component);
            Attach(component);
        }
        Changed();
    }

    private void ConnectionChanged(object? sender, PropertyChangedEventArgs change)
    {
        // One notification per route refresh, not every derived coordinate/loss property.
        if (change.PropertyName == nameof(WaveguideConnectionViewModel.PathLength)) Changed();
    }

    private void LaserChanged(object? sender, PropertyChangedEventArgs change) => Changed();
    private void GeometryChanged(object? sender, NotifyCollectionChangedEventArgs change) => Changed();
    private void Changed()
    {
        if (_canvas.Groups.IsProjecting) return;
        _canvas.Groups.MarkCurrentScopeChanged();
        _canvas.Document.Invalidate();
    }

    public void Dispose()
    {
        _canvas.Components.CollectionChanged -= ComponentsChanged;
        _canvas.Connections.CollectionChanged -= ConnectionsChanged;
        _canvas.CanvasFrozenPaths.CollectionChanged -= GeometryChanged;
        foreach (var component in _components.Keys.ToArray()) Detach(component);
        foreach (var wire in _connections.ToArray()) Detach(wire);
    }
}
