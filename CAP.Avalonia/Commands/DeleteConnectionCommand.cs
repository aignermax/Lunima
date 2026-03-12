using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for deleting a waveguide connection.
/// </summary>
public class DeleteConnectionCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly WaveguideConnectionViewModel _connectionVm;
    private readonly WaveguideConnection _connection;

    public DeleteConnectionCommand(DesignCanvasViewModel canvas, WaveguideConnectionViewModel connectionVm)
    {
        _canvas = canvas;
        _connectionVm = connectionVm;
        _connection = connectionVm.Connection;
    }

    public string Description => "Delete connection";

    public void Execute()
    {
        // Don't delete locked connections
        if (_connection.IsLocked)
            return;

        // Only remove if it's actually in the collection (to support redo)
        if (_canvas.Connections.Contains(_connectionVm))
        {
            _canvas.Connections.Remove(_connectionVm);
            _canvas.ConnectionManager.RemoveConnectionDeferred(_connection);
            _ = _canvas.RecalculateRoutesAsync();
        }
    }

    public void Undo()
    {
        // Re-add the connection
        _canvas.ConnectionManager.AddExistingConnection(_connection);
        _canvas.Connections.Add(_connectionVm);
        _ = _canvas.RecalculateRoutesAsync();
    }
}
