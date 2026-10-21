using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>Exact crossing replacements made by a conversion on the root canvas.</summary>
internal sealed class RerouteImportedRootTopology
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly HashSet<Component> _ownedComponents = new();
    private readonly HashSet<WaveguideConnection> _ownedConnections = new();
    private readonly HashSet<PhysicalPin> _touchedPins = new();
    private readonly ComponentViewModel[] _beforeComponents;
    private readonly WaveguideConnectionViewModel[] _beforeConnections;
    private readonly Dictionary<WaveguideConnection, Func<FrozenWaveguidePath>> _beforeBuilders;
    private Dictionary<WaveguideConnection, FrozenWaveguidePath> _beforePaths = new();
    private HashSet<WaveguideConnection> _replaced = new();
    private ComponentViewModel[] _removedComponents = [];
    private ComponentViewModel[] _addedComponents = [];
    private WaveguideConnectionViewModel[] _removedConnections = [];
    private WaveguideConnectionViewModel[] _addedConnections = [];
    private WaveguideConnectionViewModel[] _afterConnections = [];
    private Dictionary<WaveguideConnection, FrozenWaveguidePath> _afterPaths = new();

    /// <summary>Captures the root before routing replaces any originals.</summary>
    public RerouteImportedRootTopology(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
        _beforeComponents = canvas.Groups.RootComponents.ToArray();
        _beforeConnections = canvas.Groups.RootConnections.ToArray();
        _beforeBuilders = CapturePaths(_beforeConnections, clone: false).ToDictionary(pair => pair.Key,
            pair => CAP.Avalonia.ViewModels.Canvas.RerouteImported.RerouteImportedRoutesViewModel.CaptureTopologyPath(pair.Value));
    }

    /// <summary>Materializes detached before geometry on the snapshot worker.</summary>
    public void Prepare() => _beforePaths = _beforeBuilders.ToDictionary(pair => pair.Key, pair => pair.Value());

    /// <summary>Attributes only the routing worker's crossing mutations to this snapshot.</summary>
    public void Record(CAP_Core.Routing.CrossingInsertion.CrossingChange change)
    {
        _ownedComponents.UnionWith(change.Added);
        _ownedComponents.UnionWith(change.Removed);
        _ownedConnections.UnionWith(change.AddedConnections);
        foreach (var connection in change.AddedConnections)
        {
            _touchedPins.Add(connection.StartPin);
            _touchedPins.Add(connection.EndPin);
        }
    }

    /// <summary>Whether a target left the canvas because conversion split it.</summary>
    public bool Replaced(WaveguideConnection connection) =>
        _replaced.Contains(connection);

    /// <summary>Whether routing changed any root crossing connectivity.</summary>
    public bool HasChanges => _removedConnections.Length > 0 || _addedConnections.Length > 0
        || _removedComponents.Length > 0 || _addedComponents.Length > 0;

    /// <summary>Number of original connections replaced by crossing mutations.</summary>
    public int ReplacedCount => _removedConnections.Length;

    /// <summary>Captures crossing membership and replacement routes after routing.</summary>
    public void CaptureAfter()
    {
        var components = _canvas.Groups.RootComponents.ToArray();
        _removedComponents = _beforeComponents.Except(components)
            .Where(vm => _ownedComponents.Contains(vm.Component)).ToArray();
        _addedComponents = components.Except(_beforeComponents)
            .Where(vm => _ownedComponents.Contains(vm.Component)).ToArray();
        if (_ownedComponents.Count == 0) return;
        _afterConnections = _canvas.Groups.RootConnections.ToArray();
        var afterSet = _afterConnections.Select(vm => vm.Connection).ToHashSet();
        var beforeSet = _beforeConnections.Select(vm => vm.Connection).ToHashSet();
        var present = components.Select(vm => vm.Component).ToHashSet();
        var removedCrossings = _removedComponents.Select(vm => vm.Component).ToHashSet();
        present.UnionWith(removedCrossings);
        _removedConnections = _beforeConnections
            .Where(vm => !afterSet.Contains(vm.Connection)
                && ((_touchedPins.Contains(vm.Connection.StartPin) && _touchedPins.Contains(vm.Connection.EndPin))
                    || removedCrossings.Contains(vm.Connection.StartPin.ParentComponent)
                    || removedCrossings.Contains(vm.Connection.EndPin.ParentComponent)))
            .Where(vm => present.Contains(vm.Connection.StartPin.ParentComponent)
                && present.Contains(vm.Connection.EndPin.ParentComponent)).ToArray();
        _addedConnections = _afterConnections
            .Where(vm => !beforeSet.Contains(vm.Connection) && _ownedConnections.Contains(vm.Connection)).ToArray();
        _replaced = _removedConnections.Select(vm => vm.Connection).ToHashSet();
        _afterPaths = CapturePaths(_addedConnections, clone: true);
    }

    /// <summary>Restores one side of the root crossing replacement.</summary>
    public void Apply(bool useNew)
    {
        if (!HasChanges) return;
        var paths = useNew ? _afterPaths : _beforePaths;
        foreach (var vm in useNew ? _addedConnections : _removedConnections)
        {
            if (!paths.TryGetValue(vm.Connection, out var path)) continue;
            vm.Connection.RestoreCachedPath(path.Path.DeepCopy());
            path.ApplySettingsTo(vm.Connection);
        }
        _canvas.Groups.ApplyRootTopology(
            useNew ? _removedComponents : _addedComponents,
            useNew ? _addedComponents : _removedComponents,
            useNew ? _removedConnections : _addedConnections,
            useNew ? _addedConnections : _removedConnections,
            useNew ? _afterConnections : _beforeConnections);
    }

    private static Dictionary<WaveguideConnection, FrozenWaveguidePath> CapturePaths(
        IEnumerable<WaveguideConnectionViewModel> connections, bool clone)
    {
        var paths = new Dictionary<WaveguideConnection, FrozenWaveguidePath>();
        foreach (var vm in connections)
        {
            var connection = vm.Connection;
            if (connection.RoutedPath is not { } route) continue;
            var path = new FrozenWaveguidePath
            {
                StartPin = connection.StartPin, EndPin = connection.EndPin,
                Path = clone ? route.DeepCopy() : route
            };
            path.CaptureSettingsFrom(connection);
            paths[connection] = path;
        }
        return paths;
    }
}
