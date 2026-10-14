using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Components.PinKinds;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for creating a waveguide connection between two pins.
/// Tracks and restores any connections that were overwritten.
/// Cross-domain pairs (optical ↔ electrical) are rejected: <see cref="Execute"/> is a no-op
/// for them — a defensive backstop. The UI connect paths (drag gesture, click-to-connect)
/// pre-check <see cref="PinKindHelper.AreKindsCompatible"/> and do not issue this command for
/// an incompatible pair (so no empty undo entry is created); this guard only catches a caller
/// that skips that check.
/// </summary>
public class CreateConnectionCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly PhysicalPin _startPin;
    private readonly PhysicalPin _endPin;
    private WaveguideConnection? _connection;
    private WaveguideConnectionViewModel? _connectionViewModel;

    // Connections that were removed when creating this connection. Crossing
    // sub-connections are stored as their pre-split ORIGINALS (#705): restoring a
    // sub whose crossing was dissolved would leave ghost pins on a dead component.
    private List<WaveguideConnection>? _removedConnections;

    private readonly Ownership.WireHomingStep _homing;

    public CreateConnectionCommand(
        DesignCanvasViewModel canvas,
        PhysicalPin startPin,
        PhysicalPin endPin)
    {
        _canvas = canvas;
        _homing = new Ownership.WireHomingStep(canvas);
        _startPin = InsideDie(startPin);
        _endPin = InsideDie(endPin);
    }

    /// <summary>
    /// A die's boundary pin stands for the part pin it exposes; wires on the assembly attach
    /// to that part pin (the die's synthesized pins are rebuilt with its S-matrix).
    /// </summary>
    private static PhysicalPin InsideDie(PhysicalPin pin) =>
        pin.ParentComponent is ComponentGroup { IsDie: true } die
            && die.ExternalPins.FirstOrDefault(p => p.Name == pin.Name)?.InternalPin is { } inner
            ? inner
            : pin;

    /// <summary>
    /// True when the request is refused because it would run a waveguide across a die edge
    /// (see <see cref="CAP_Core.Components.Dies.DieWiringPolicy"/>).
    /// </summary>
    public bool CrossesDieEdge =>
        CAP_Core.Components.Dies.DieWiringPolicy.Evaluate(_startPin, _endPin).RefusalKey != null;

    /// <summary>
    /// Completes when a wire that belongs to a group below the current level has been routed
    /// and stored in that group (immediately for every other wire).
    /// </summary>
    public Task HomingCompletion { get; private set; } = Task.CompletedTask;

    public string Description => $"Connect {_startPin.Name} to {_endPin.Name}";

    /// <summary>
    /// True when both pins belong to the same signal domain and may be connected.
    /// </summary>
    public bool ArePinKindsCompatible => PinKindHelper.AreKindsCompatible(_startPin, _endPin);

    public void Execute()
    {
        // Optical ↔ electrical connections are physically meaningless — refuse to create them
        // regardless of which UI path (drag gesture, click-to-connect, …) issued the command.
        // A waveguide across a die edge is refused too: dies connect only through couplers.
        if (!ArePinKindsCompatible || CrossesDieEdge)
            return;

        if (_connection != null && _connectionViewModel != null)
        {
            // Redo: remove any restored connections first, then re-add the new connection —
            // into its owning group when it was stored there.
            RemoveReplacedConnections();
            if (_homing.HasHome)
            {
                _homing.Rehome();
                _canvas.InvalidateSimulation();
                return;
            }
            _canvas.ConnectionManager.AddExistingConnection(_connection);
            _canvas.Connections.Add(_connectionViewModel);
            HomingCompletion = _homing.HomeWhenRoutedAsync(_connectionViewModel);
        }
        else
        {
            // First execution: track connections that will be removed
            _removedConnections = new List<WaveguideConnection>();
            var crossing = _canvas.ConnectionManager.CrossingService;

            // A replaced piece of a routing-placed crossing chain leaves as its whole wire.
            if (!ChainCrossingMaintenance.IsTemporaryCrossing(_startPin.ParentComponent)
                && !ChainCrossingMaintenance.IsTemporaryCrossing(_endPin.ParentComponent))
                _canvas.DissolveInsertedCrossingChains(TouchesEitherPin);

            // Find connections on start pin
            var startConnections = _canvas.Connections
                .Where(c => c.Connection.StartPin == _startPin || c.Connection.EndPin == _startPin)
                .ToList();

            // Find connections on end pin
            var endConnections = _canvas.Connections
                .Where(c => c.Connection.StartPin == _endPin || c.Connection.EndPin == _endPin)
                .ToList();

            // Store all connections that will be removed, normalized to their
            // pre-split originals (two subs of the same crossing dedupe to one).
            foreach (var connVm in startConnections.Concat(endConnections).Distinct())
            {
                var original = crossing?.ResolveToOriginal(connVm.Connection) ?? connVm.Connection;
                if (!_removedConnections.Contains(original))
                    _removedConnections.Add(original);
            }

            // Create new connection (this will remove the old ones)
            _connectionViewModel = _canvas.ConnectPins(_startPin, _endPin);
            if (_connectionViewModel != null)
            {
                _connection = _connectionViewModel.Connection;
                // A wire between two parts of a group below this level belongs to that group.
                HomingCompletion = _homing.HomeWhenRoutedAsync(_connectionViewModel);
            }
        }
        // Trigger async re-routing so the UI doesn't block
        _ = _canvas.RecalculateRoutesAsync();
    }

    public void Undo()
    {
        if (_connection != null && _connectionViewModel != null)
        {
            _homing.Cancel();
            if (_homing.IsHomed)
            {
                _homing.Unhome();
            }
            else
            {
                // Remove the new connection. If the crossing pass split it, this
                // dissolves the crossing instead of removing a stale object (#705).
                _canvas.ConnectionManager.RemoveConnectionDeferred(_connection);
                _canvas.Connections.Remove(_connectionViewModel);
            }

            // Restore any connections that were removed
            if (_removedConnections != null)
            {
                foreach (var conn in _removedConnections)
                {
                    _canvas.ConnectionManager.AddExistingConnection(conn);
                    if (!_canvas.Connections.Any(c => c.Connection == conn))
                        _canvas.Connections.Add(new WaveguideConnectionViewModel(conn));
                }
            }

            _canvas.InvalidateSimulation();
            _ = _canvas.RecalculateRoutesAsync();
        }
    }

    private void RemoveReplacedConnections()
    {
        if (_removedConnections == null)
            return;
        foreach (var conn in _removedConnections)
        {
            var vm = _canvas.Connections.FirstOrDefault(c => c.Connection == conn);
            if (vm != null)
                _canvas.Connections.Remove(vm);
            _canvas.ConnectionManager.RemoveConnectionDeferred(conn);
        }
    }
    private bool TouchesEitherPin(WaveguideConnection connection) =>
        connection.StartPin == _startPin || connection.EndPin == _startPin
        || connection.StartPin == _endPin || connection.EndPin == _endPin;
}
