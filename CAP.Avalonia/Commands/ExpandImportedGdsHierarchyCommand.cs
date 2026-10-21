using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Commands;

/// <summary>Atomically exchanges one imported instance for its original source children.</summary>
public sealed class ExpandImportedGdsHierarchyCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentViewModel _original;
    private readonly IReadOnlyList<ComponentViewModel> _children;
    private readonly IReadOnlyList<CanvasFrozenPathViewModel> _paths;
    private readonly IReadOnlyList<WaveguideConnectionViewModel> _connections;
    private readonly Action _register;
    private readonly string _interfaceSnapshot;
    private readonly bool _isObstacle;
    private readonly (double X, double Y, double Rotation, bool Mirror, double Width, double Height) _pose;

    internal ExpandImportedGdsHierarchyCommand(DesignCanvasViewModel canvas, ComponentViewModel original,
        IReadOnlyList<ComponentViewModel> children, IReadOnlyList<CanvasFrozenPathViewModel> paths,
        IReadOnlyList<WaveguideConnectionViewModel> connections, Action register)
    {
        _canvas = canvas; _original = original; _children = children; _paths = paths;
        _connections = connections; _register = register;
        var c = original.Component;
        _interfaceSnapshot = InterfaceSnapshot(original);
        _isObstacle = c.IsRoutingObstacle;
        _pose = (c.PhysicalX, c.PhysicalY, c.RotationDegrees, c.IsMirroredHorizontally,
            c.WidthMicrometers, c.HeightMicrometers);
    }

    /// <inheritdoc />
    public string Description => $"Expose source children of {_original.DisplayName}";
    /// <summary>The prepared source child instances, reused across undo and redo.</summary>
    public IReadOnlyList<ComponentViewModel> Children => _children;

    /// <inheritdoc />
    public void Execute()
    {
        GdsHierarchyExpansionService.ValidateSelection(_canvas, _original);
        var c = _original.Component;
        if (_pose != (c.PhysicalX, c.PhysicalY, c.RotationDegrees, c.IsMirroredHorizontally,
            c.WidthMicrometers, c.HeightMicrometers))
            throw new InvalidOperationException("The component moved while preparing expansion. Try again.");
        if (_interfaceSnapshot != InterfaceSnapshot(_original) || _isObstacle != c.IsRoutingObstacle)
            throw new InvalidOperationException("The component properties changed while preparing expansion. Try again.");
        _register();
        _canvas.BeginCommandExecution();
        try
        {
            Detach(_original);
            foreach (var child in _children) Attach(child);
            foreach (var path in _paths) _canvas.CanvasFrozenPaths.Add(path);
            foreach (var connection in _connections)
            {
                _canvas.ConnectionManager.AddExistingConnection(connection.Connection);
                _canvas.Connections.Add(connection);
            }
            _canvas.Selection.ClearSelection();
            foreach (var child in _children) _canvas.Selection.AddToSelection(child);
        }
        catch
        {
            RestoreOriginal();
            throw;
        }
        finally { _canvas.EndCommandExecution(); }
        _canvas.InvalidateSimulation();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _canvas.BeginCommandExecution();
        try { RestoreOriginal(); }
        finally { _canvas.EndCommandExecution(); }
        _canvas.InvalidateSimulation();
    }

    private static string InterfaceSnapshot(ComponentViewModel vm) => string.Join(";", vm.Component.PhysicalPins.Select(p =>
        FormattableString.Invariant($"{p.Name}:{p.OffsetXMicrometers:R}:{p.OffsetYMicrometers:R}:{p.AngleDegrees:R}:{p.MatterType}:{p.Layer}:{p.WaveguideWidthMicrometers}:{p.PortType}")));

    private void RestoreOriginal()
    {
        foreach (var connection in _connections)
        {
            _canvas.Connections.Remove(connection);
            _canvas.ConnectionManager.RemoveConnectionDeferred(connection.Connection);
        }
        foreach (var path in _paths) _canvas.CanvasFrozenPaths.Remove(path);
        foreach (var child in _children) { child.IsSelected = false; Detach(child); }
        Attach(_original);
        _canvas.Selection.SelectSingle(_original);
    }

    // Keep the actual VMs, pin identities, source PDK and laser settings across undo/redo.
    // RemoveComponent would schedule unrelated routing during the atomic exchange.
    private void Detach(ComponentViewModel vm)
    {
        _canvas.Router.RemoveComponentObstacle(vm.Component);
        foreach (var pin in _canvas.AllPins.Where(p => p.ParentComponentViewModel == vm).ToList())
            _canvas.AllPins.Remove(pin);
        _canvas.Components.Remove(vm);
    }

    private void Attach(ComponentViewModel vm)
    {
        if (_canvas.Components.Contains(vm)) return;
        vm.OnSliderChanged = () => _canvas.RequestResimulation();
        _canvas.Components.Add(vm);
        _canvas.Router.AddComponentObstacle(vm.Component);
        foreach (var pin in vm.Component.PhysicalPins) _canvas.AllPins.Add(new PinViewModel(pin, vm));
    }
}
