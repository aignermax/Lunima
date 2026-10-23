namespace CAP.Avalonia.ViewModels.Canvas.Services;

public partial class GroupEditService
{
    /// <summary>The design root, even while a group's contents occupy the canvas.</summary>
    public IEnumerable<ComponentViewModel> RootComponents =>
        _rootCanvasBackup is { } backup ? backup.Components : _components;

    /// <summary>Preserves current independent edits before applying a stored group history delta.</summary>
    public void SaveEditedGroup(CAP_Core.Components.Core.ComponentGroup group)
    {
        if (ReferenceEquals(CurrentEditGroup, group)) _content.Synchronize();
    }

    /// <summary>Refreshes an open group after its stored topology was restored by history.</summary>
    public void RefreshEditedGroup(CAP_Core.Components.Core.ComponentGroup group)
    {
        if (ReferenceEquals(CurrentEditGroup, group)) LoadGroupAsSubCanvas(group);
    }

    /// <summary>Replays a root topology change even while another group occupies the canvas.</summary>
    public void ApplyRootTopology(
        IReadOnlyCollection<ComponentViewModel> removedComponents,
        IReadOnlyCollection<ComponentViewModel> addedComponents,
        IReadOnlyCollection<WaveguideConnectionViewModel> removedConnections,
        IReadOnlyCollection<WaveguideConnectionViewModel> addedConnections,
        IReadOnlyList<WaveguideConnectionViewModel> order)
    {
        IList<ComponentViewModel> components = _rootCanvasBackup is { } root ? root.Components : _components;
        IList<WaveguideConnectionViewModel> connections = _rootCanvasBackup is { } backup ? backup.Connections : _connections;
        IList<PinViewModel> pins = _rootCanvasBackup is { } saved ? saved.AllPins : _allPins;
        foreach (var component in removedComponents)
        {
            components.Remove(component);
            foreach (var pin in pins.Where(p => p.ParentComponentViewModel == component).ToList())
                pins.Remove(pin);
        }
        foreach (var component in addedComponents.Where(c => !components.Contains(c)))
        {
            components.Add(component);
            foreach (var pin in component.Component.PhysicalPins)
                pins.Add(new PinViewModel(pin, component));
        }
        var removed = removedConnections.Select(vm => vm.Connection).ToHashSet();
        foreach (var connection in connections.Where(vm => removed.Contains(vm.Connection)).ToList())
            connections.Remove(connection);
        var present = connections.Select(vm => vm.Connection).ToHashSet();
        foreach (var connection in addedConnections)
            if (present.Add(connection.Connection)) connections.Add(connection);
        var ordered = order.Select((vm, index) => (vm.Connection, index)).ToDictionary(pair => pair.Connection, pair => pair.index);
        var sorted = connections.OrderBy(vm => ordered.GetValueOrDefault(vm.Connection, int.MaxValue)).ToList();
        connections.Clear();
        foreach (var connection in sorted) connections.Add(connection);
        if (_rootCanvasBackup is { } state)
            state.ManagerConnections = sorted.Select(vm => vm.Connection).ToList();
        else
        {
            _connectionManager.SwapConnections(removed.ToArray(), addedConnections.Select(vm => vm.Connection).ToArray());
            _connectionManager.RestoreConnectionOrder(sorted.Select(vm => vm.Connection).ToList());
        }
    }

    /// <summary>The root's wires, excluding the currently edited group's internal wires.</summary>
    public IEnumerable<WaveguideConnectionViewModel> RootConnections =>
        _rootCanvasBackup is { } backup ? backup.Connections : _connections;
}
