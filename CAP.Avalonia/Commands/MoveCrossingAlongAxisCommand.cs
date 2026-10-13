using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Slides a crossing along the axis of one of its two wires, as one undoable step. The wire
/// along the axis keeps its route — its two pieces only stretch or shrink the straight into
/// the crossing (see <see cref="CrossingAxisMovePlanner"/>); the wire across the axis, and any
/// piece that cannot be patched, routes again in place, so the routing-placed crossings it
/// docks onto stay. A routing-placed crossing the user moves becomes user-owned: its
/// <see cref="Component.IsInsertedCrossing"/> flag (and any crossing record) is dropped, so
/// the router never takes a deliberate placement out again; undo puts both back. Crossing
/// changes the follow-up re-route makes are reverted by the crossing undo journal, which
/// attributes them to this command.
/// </summary>
public sealed class MoveCrossingAlongAxisCommand : IUndoableCommand
{
    private const double MinimumMoveMicrometers = 1e-3;

    private readonly DesignCanvasViewModel _canvas;
    private readonly Component _crossing;
    private readonly CrossingAxis _axis;
    private readonly double _deltaMicrometers;
    private readonly ErrorConsoleService? _errorConsole;

    private List<PieceSnapshot> _snapshots = new();
    private CrossingRecord? _releasedRecord;
    private bool _wasInsertedCrossing;
    private bool _applied;

    /// <summary>Creates the move of <paramref name="crossing"/> by <paramref name="deltaMicrometers"/> along <paramref name="axis"/>.</summary>
    /// <param name="canvas">The canvas the crossing is on.</param>
    /// <param name="crossing">A crossing component on the canvas.</param>
    /// <param name="axis">Which of its wires to slide along.</param>
    /// <param name="deltaMicrometers">Signed distance along the axis (+X right, +Y down).</param>
    /// <param name="errorConsole">Optional sink for failures of the follow-up routing pass.</param>
    public MoveCrossingAlongAxisCommand(
        DesignCanvasViewModel canvas, Component crossing, CrossingAxis axis, double deltaMicrometers,
        ErrorConsoleService? errorConsole = null)
    {
        _canvas = canvas;
        _crossing = crossing;
        _axis = axis;
        _deltaMicrometers = deltaMicrometers;
        _errorConsole = errorConsole;
    }

    /// <inheritdoc/>
    public string Description => $"Move crossing {_crossing.Identifier}";

    /// <summary>True when the last <see cref="Execute"/> moved the crossing.</summary>
    public bool Succeeded => _applied;

    /// <summary>Why the last <see cref="Execute"/> was a no-op (null after a successful move).</summary>
    public string? Error { get; private set; }

    /// <summary>The plan of the last successful <see cref="Execute"/>: which pieces were patched and which re-routed.</summary>
    public CrossingAxisMovePlan? LastPlan { get; private set; }

    /// <summary>The routing pass the last Execute/Undo started; completes when the design is routed.</summary>
    public Task RerouteTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Why moving <paramref name="crossing"/> by <paramref name="deltaMicrometers"/> along
    /// <paramref name="axis"/> is not possible, or null when it is: it must be an unlocked
    /// crossing on the canvas, the move must be noticeable, and the moved footprint must
    /// neither overlap another component nor sit on a wire that does not dock on it.
    /// </summary>
    public static string? Validate(DesignCanvasViewModel canvas, Component crossing, CrossingAxis axis, double deltaMicrometers)
    {
        if (!CrossingComponentCatalog.IsCrossing(crossing)) return "Component is not a crossing.";
        var vm = canvas.Components.FirstOrDefault(c => c.Component == crossing);
        if (vm == null) return "Crossing is not on the canvas.";
        if (crossing.IsLocked) return "Crossing is locked.";
        if (!double.IsFinite(deltaMicrometers) || Math.Abs(deltaMicrometers) < MinimumMoveMicrometers)
            return "Move distance is too small.";
        var delta = CrossingAxisMovePlanner.DeltaFor(axis, deltaMicrometers);
        if (!canvas.CanPlaceComponent(vm.X + delta.X, vm.Y + delta.Y, vm.Width, vm.Height, vm))
            return "The moved crossing would overlap another component.";
        var wire = CrossingAxisMovePlanner.FindWireUnderMovedFootprint(crossing, delta, SnapshotConnections(canvas));
        return wire == null ? null : $"The moved crossing would sit on wire {wire.StartPin.Name}–{wire.EndPin.Name}.";
    }

    /// <inheritdoc/>
    public void Execute()
    {
        _applied = false;
        Error = Validate(_canvas, _crossing, _axis, _deltaMicrometers);
        if (Error != null) return;

        var manager = _canvas.ConnectionManager;
        var plan = CrossingAxisMovePlanner.Plan(_crossing, SnapshotConnections(_canvas), _axis, _deltaMicrometers);
        _snapshots = plan.Patched.Select(p => p.Piece).Concat(plan.Rerouted).Select(PieceSnapshot.Of).ToList();
        _wasInsertedCrossing = _crossing.IsInsertedCrossing;
        _crossing.IsInsertedCrossing = false;
        _releasedRecord = manager.CrossingService?.ReleaseRecord(_crossing);

        Translate(plan.Delta.X, plan.Delta.Y);
        foreach (var patched in plan.Patched)
            ApplyRoute(patched.Piece, patched.Path);
        foreach (var piece in plan.Rerouted)
            manager.RerouteInPlace(piece);

        LastPlan = plan;
        _applied = true;
        Refresh();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (!_applied || LastPlan == null) return;
        Translate(-LastPlan.Delta.X, -LastPlan.Delta.Y);
        _crossing.IsInsertedCrossing = _wasInsertedCrossing;
        if (_releasedRecord != null)
            _canvas.ConnectionManager.CrossingService?.RestoreRecord(_releasedRecord);

        var present = SnapshotConnections(_canvas).ToHashSet();
        foreach (var snapshot in _snapshots.Where(s => present.Contains(s.Piece)))
        {
            snapshot.Restore();
            UpdateObstacle(snapshot.Piece);
        }
        _applied = false;
        Refresh();
    }

    private void Translate(double dx, double dy)
    {
        var vm = _canvas.Components.FirstOrDefault(c => c.Component == _crossing);
        _canvas.Router.RemoveComponentObstacle(_crossing);
        _crossing.PhysicalX += dx;
        _crossing.PhysicalY += dy;
        if (vm != null)
        {
            vm.X = _crossing.PhysicalX;
            vm.Y = _crossing.PhysicalY;
        }
        _canvas.Router.AddComponentObstacle(_crossing);
    }

    private void ApplyRoute(WaveguideConnection piece, RoutedPath path)
    {
        piece.RestoreCachedPath(path);
        UpdateObstacle(piece);
    }

    private void UpdateObstacle(WaveguideConnection piece)
    {
        var grid = _canvas.Router.PathfindingGrid;
        if (grid == null) return;
        grid.RemoveWaveguideObstacle(piece.Id);
        if (piece.IsPathValid && piece.RoutedPath != null)
            grid.AddWaveguideObstacle(piece.Id, piece.RoutedPath.Segments, _canvas.ConnectionManager.WaveguideWidthMicrometers);
    }

    private void Refresh()
    {
        foreach (var vm in _canvas.Connections)
            vm.NotifyPathChanged();
        _canvas.InvalidateSimulation();
        RerouteTask = ObserveRerouteAsync();
    }

    /// <summary>
    /// Runs the re-route without blocking Execute/Undo on it (awaiting here could deadlock
    /// against the pass's dispatcher-posted continuations); failures reach the error console.
    /// </summary>
    private async Task ObserveRerouteAsync()
    {
        try
        {
            await _canvas.RecalculateRoutesAsync();
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError("Moving a crossing: routing afterwards failed", ex);
        }
    }

    private static List<WaveguideConnection> SnapshotConnections(DesignCanvasViewModel canvas)
    {
        lock (canvas.ConnectionManager.SyncRoot)
            return canvas.ConnectionManager.Connections.ToList();
    }

    /// <summary>A piece's route and manual edits before the move — the move only ever replaces the route object.</summary>
    private sealed record PieceSnapshot(
        WaveguideConnection Piece, RoutedPath? Route, bool IsRouteFrozen,
        Dictionary<int, double> BendRadiusOverrides, Dictionary<int, double> StraightShiftOffsets)
    {
        public static PieceSnapshot Of(WaveguideConnection piece) => new(
            piece, piece.RoutedPath, piece.IsRouteFrozen,
            new Dictionary<int, double>(piece.BendRadiusOverrides), new Dictionary<int, double>(piece.StraightShiftOffsets));

        public void Restore()
        {
            if (Route != null)
                Piece.RestoreCachedPath(Route);
            else
                Piece.InvalidateRoute();
            Piece.IsRouteFrozen = IsRouteFrozen;
            Refill(Piece.BendRadiusOverrides, BendRadiusOverrides);
            Refill(Piece.StraightShiftOffsets, StraightShiftOffsets);
        }

        private static void Refill(Dictionary<int, double> target, Dictionary<int, double> source)
        {
            target.Clear();
            foreach (var (key, value) in source)
                target[key] = value;
        }
    }
}
