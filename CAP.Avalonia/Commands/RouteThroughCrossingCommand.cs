using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Re-docks a wire onto the two free ports of a crossing that already splits another wire
/// (e.g. one just inserted by <see cref="InsertManualCrossingCommand"/>): the wire is replaced
/// by two pieces — its start pin to the crossing port facing it, the opposite port to its end
/// pin — so it passes straight through the other wire instead of having to go around it.
/// Undo restores the original connection object. A no-op when the crossing is not on the
/// canvas, the wire is gone, or the free ports are already taken.
/// </summary>
public sealed class RouteThroughCrossingCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly WaveguideConnection _original;
    private readonly Component _crossing;
    private readonly bool _freeAxisIsHorizontal;
    private List<WaveguideConnection>? _pieces;

    /// <summary>Creates the command.</summary>
    /// <param name="canvas">The design canvas.</param>
    /// <param name="through">The wire to route through the crossing.</param>
    /// <param name="crossing">The crossing component (already placed).</param>
    /// <param name="cutSegmentIsHorizontal">Whether the wire the crossing splits runs along X there;
    /// the free ports are on the other axis.</param>
    public RouteThroughCrossingCommand(DesignCanvasViewModel canvas, WaveguideConnection through,
        Component crossing, bool cutSegmentIsHorizontal)
    {
        _canvas = canvas;
        _original = through;
        _crossing = crossing;
        _freeAxisIsHorizontal = !cutSegmentIsHorizontal;
    }

    /// <inheritdoc/>
    public string Description => $"Route {_original.StartPin.Name}–{_original.EndPin.Name} through crossing";

    /// <summary>Whether the last <see cref="Execute"/> re-docked the wire.</summary>
    public bool Applied => _pieces != null && _canvas.ConnectionManager.Connections.Contains(_pieces[0]);

    /// <inheritdoc/>
    public void Execute()
    {
        _pieces ??= TryBuildPieces();
        if (_pieces == null || !_canvas.ConnectionManager.Connections.Contains(_original))
            return;

        Remove(_original);
        foreach (var piece in _pieces)
            _canvas.RestoreConnectionAndViewModel(piece);
        _canvas.InvalidateSimulation();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (_pieces == null || !_canvas.ConnectionManager.Connections.Contains(_pieces[0]))
            return;

        foreach (var piece in _pieces)
            Remove(piece);
        _canvas.RestoreConnectionAndViewModel(_original);
        _canvas.InvalidateSimulation();
    }

    private List<WaveguideConnection>? TryBuildPieces()
    {
        if (!_canvas.Components.Any(vm => vm.Component == _crossing)
            || !_canvas.ConnectionManager.Connections.Contains(_original))
            return null;

        var (entry, exit) = CrossingPlacement.ResolveThroughPorts(_crossing, _freeAxisIsHorizontal, TravelDirection());
        if (entry?.LogicalPin == null || exit?.LogicalPin == null || IsTaken(entry) || IsTaken(exit))
            return null;

        return new List<WaveguideConnection>
        {
            CrossingPlacement.CreateSubConnection(_original, _original.StartPin, entry),
            CrossingPlacement.CreateSubConnection(_original, exit, _original.EndPin),
        };
    }

    /// <summary>Unit travel direction along the free axis: from the wire's start towards the crossing.</summary>
    private (double X, double Y) TravelDirection()
    {
        var centerX = _crossing.PhysicalX + _crossing.WidthMicrometers / 2.0;
        var centerY = _crossing.PhysicalY + _crossing.HeightMicrometers / 2.0;
        var (startX, startY) = _original.StartPin.GetAbsolutePosition();
        var (endX, endY) = _original.EndPin.GetAbsolutePosition();
        var towards = _freeAxisIsHorizontal ? centerX - startX : centerY - startY;
        if (Math.Abs(towards) < 1e-9)
            towards = _freeAxisIsHorizontal ? endX - centerX : endY - centerY;
        var sign = towards >= 0 ? 1.0 : -1.0;
        return _freeAxisIsHorizontal ? (sign, 0) : (0, sign);
    }

    private bool IsTaken(PhysicalPin port) =>
        _canvas.ConnectionManager.Connections.Any(c => c.StartPin == port || c.EndPin == port);

    private void Remove(WaveguideConnection connection)
    {
        _canvas.ConnectionManager.RemoveConnectionDeferred(connection);
        var vm = _canvas.Connections.FirstOrDefault(c => c.Connection == connection);
        if (vm != null)
            _canvas.Connections.Remove(vm);
    }
}
