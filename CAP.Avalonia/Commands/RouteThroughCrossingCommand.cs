using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Re-docks a wire onto the two free ports of a crossing that already splits another wire
/// (e.g. one just inserted by <see cref="InsertManualCrossingCommand"/>): the wire is replaced
/// by two pieces — its start pin to the free port facing it, the opposite port to its end pin —
/// so it passes straight through the other wire instead of having to go around it (the usual
/// way to swap the order of two wires). The pieces are route-less, so the next pass routes
/// them. Undo restores the original connection object. A no-op when the crossing is not on the
/// canvas, the wire is gone, or the crossing has no free opposite port pair.
/// </summary>
public sealed class RouteThroughCrossingCommand : IUndoableCommand
{
    private const double OppositeDotThreshold = -0.9;

    private readonly DesignCanvasViewModel _canvas;
    private readonly WaveguideConnection _original;
    private readonly Component _crossing;
    private List<WaveguideConnection>? _pieces;

    /// <summary>Creates the command.</summary>
    /// <param name="canvas">The design canvas.</param>
    /// <param name="through">The wire to route through the crossing.</param>
    /// <param name="crossing">The crossing component (placed before this command runs).</param>
    public RouteThroughCrossingCommand(DesignCanvasViewModel canvas, WaveguideConnection through, Component crossing)
    {
        _canvas = canvas;
        _original = through;
        _crossing = crossing;
    }

    /// <inheritdoc/>
    public string Description => $"Route {_original.StartPin.Name}–{_original.EndPin.Name} through crossing";

    /// <summary>Whether the wire is currently docked on the crossing (after <see cref="Execute"/>).</summary>
    public bool Applied => _pieces != null && _canvas.ConnectionManager.Connections.Contains(_pieces[0]);

    /// <inheritdoc/>
    public void Execute()
    {
        _pieces ??= TryBuildPieces();
        if (_pieces == null || !_canvas.ConnectionManager.Connections.Contains(_original))
            return;
        Swap(new[] { _original }, _pieces);
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (!Applied)
            return;
        Swap(_pieces!, new[] { _original });
    }

    private List<WaveguideConnection>? TryBuildPieces()
    {
        if (!_canvas.Components.Any(vm => vm.Component == _crossing)
            || !_canvas.ConnectionManager.Connections.Contains(_original))
            return null;

        var free = _crossing.PhysicalPins.Where(p => p.LogicalPin != null && !IsTaken(p)).ToList();
        var pair = OppositePair(free);
        if (pair == null)
            return null;

        var (entry, exit) = OrderByStart(pair.Value.A, pair.Value.B);
        return new List<WaveguideConnection>
        {
            CrossingPlacement.CreateSubConnection(_original, _original.StartPin, entry),
            CrossingPlacement.CreateSubConnection(_original, exit, _original.EndPin),
        };
    }

    private static (PhysicalPin A, PhysicalPin B)? OppositePair(IReadOnlyList<PhysicalPin> ports)
    {
        for (var i = 0; i < ports.Count; i++)
            for (var j = i + 1; j < ports.Count; j++)
                if (Dot(Facing(ports[i]), Facing(ports[j])) < OppositeDotThreshold)
                    return (ports[i], ports[j]);
        return null;
    }

    /// <summary>The entry is the free port that faces the wire's start pin; the exit is its opposite.</summary>
    private (PhysicalPin Entry, PhysicalPin Exit) OrderByStart(PhysicalPin a, PhysicalPin b)
    {
        var (sx, sy) = _original.StartPin.GetAbsolutePosition();
        var (ax, ay) = a.GetAbsolutePosition();
        var towardsStart = (sx - ax, sy - ay);
        return Dot(Facing(a), towardsStart) >= Dot(Facing(b), towardsStart) ? (a, b) : (b, a);
    }

    private static (double X, double Y) Facing(PhysicalPin pin)
    {
        var radians = pin.GetAbsoluteAngle() * Math.PI / 180.0;
        return (Math.Cos(radians), Math.Sin(radians));
    }

    private static double Dot((double X, double Y) a, (double X, double Y) b) => a.X * b.X + a.Y * b.Y;

    private bool IsTaken(PhysicalPin port) =>
        _canvas.ConnectionManager.Connections.Any(c => c.StartPin == port || c.EndPin == port);

    private void Swap(IReadOnlyCollection<WaveguideConnection> removed, IReadOnlyCollection<WaveguideConnection> added)
    {
        _canvas.ConnectionManager.SwapConnections(removed, added);
        foreach (var vm in _canvas.Connections.Where(c => removed.Contains(c.Connection)).ToList())
            _canvas.Connections.Remove(vm);
        foreach (var connection in added.Where(c => !_canvas.Connections.Any(vm => vm.Connection == c)))
            _canvas.Connections.Add(new WaveguideConnectionViewModel(connection));
        _canvas.InvalidateSimulation();
    }
}
