using CAP.Avalonia.ViewModels;
using CAP_Core.Components;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for deleting a component from the canvas.
/// </summary>
public class DeleteComponentCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentViewModel _componentViewModel;
    private readonly Component _component;
    private readonly double _x;
    private readonly double _y;
    private readonly List<(WaveguideConnection connection, WaveguideConnectionViewModel vm)> _deletedConnections = new();

    public DeleteComponentCommand(
        DesignCanvasViewModel canvas,
        ComponentViewModel componentViewModel)
    {
        _canvas = canvas;
        _componentViewModel = componentViewModel;
        _component = componentViewModel.Component;
        _x = componentViewModel.X;
        _y = componentViewModel.Y;
    }

    public string Description => $"Delete {_component.Identifier}";

    public void Execute()
    {
        // Store connections that will be deleted
        _deletedConnections.Clear();
        foreach (var connVm in _canvas.Connections.ToList())
        {
            if (connVm.Connection.StartPin.ParentComponent == _component ||
                connVm.Connection.EndPin.ParentComponent == _component)
            {
                _deletedConnections.Add((connVm.Connection, connVm));
            }
        }

        _canvas.RemoveComponent(_componentViewModel);
    }

    public void Undo()
    {
        // Re-add the component
        _component.PhysicalX = _x;
        _component.PhysicalY = _y;
        var newVm = _canvas.AddComponent(_component);

        // Re-add connections
        foreach (var (connection, _) in _deletedConnections)
        {
            _canvas.ConnectionManager.Connections.Add(connection);
            var connVm = new WaveguideConnectionViewModel(connection);
            _canvas.Connections.Add(connVm);
        }
    }
}
