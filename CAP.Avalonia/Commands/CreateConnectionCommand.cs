using CAP.Avalonia.ViewModels;
using CAP_Core.Components;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for creating a waveguide connection between two pins.
/// </summary>
public class CreateConnectionCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly PhysicalPin _startPin;
    private readonly PhysicalPin _endPin;
    private WaveguideConnection? _connection;
    private WaveguideConnectionViewModel? _connectionViewModel;

    public CreateConnectionCommand(
        DesignCanvasViewModel canvas,
        PhysicalPin startPin,
        PhysicalPin endPin)
    {
        _canvas = canvas;
        _startPin = startPin;
        _endPin = endPin;
    }

    public string Description => $"Connect {_startPin.Name} to {_endPin.Name}";

    public void Execute()
    {
        _connectionViewModel = _canvas.ConnectPins(_startPin, _endPin);
        if (_connectionViewModel != null)
        {
            _connection = _connectionViewModel.Connection;
        }
    }

    public void Undo()
    {
        if (_connection != null && _connectionViewModel != null)
        {
            _canvas.ConnectionManager.Connections.Remove(_connection);
            _canvas.Connections.Remove(_connectionViewModel);
            _connection = null;
            _connectionViewModel = null;
        }
    }
}
