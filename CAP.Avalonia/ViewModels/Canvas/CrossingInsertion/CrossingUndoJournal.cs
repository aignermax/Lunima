using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;

/// <summary>
/// Makes undo restore the crossings exactly: the re-route after an edit takes out the
/// router-placed crossings whose wires went stale (and, with adaptive insertion on, places
/// new ones). Those changes are not part of the edit's own command, so this journal records
/// them (<see cref="CrossingInsertionService.ChangeRecorded"/>) against the history entry
/// that caused them and reverts them, newest first, just before that entry is undone. A redo
/// runs the edit again and its re-route makes the same changes anew.
/// </summary>
public sealed class CrossingUndoJournal
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Action<Component> _crossingRestored;
    private readonly Action<Component> _crossingDropped;
    private readonly Dictionary<IUndoableCommand, List<CrossingChange>> _changes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IUndoableCommand, PassOwner> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly object _sync = new();
    // Task.Run inherits the command that started its pass. A later UI edit must not
    // steal crossing changes from the still-finishing previous routing worker.
    private readonly AsyncLocal<PassOwner?> _current = new();
    private int _historyGeneration;
    private sealed record PassOwner(IUndoableCommand Command, int Generation);

    /// <summary>Creates the journal for <paramref name="canvas"/>.</summary>
    /// <param name="canvas">The canvas whose connection manager the changes apply to.</param>
    /// <param name="crossingRestored">Shows a crossing an undo put back (UI thread).</param>
    /// <param name="crossingDropped">Hides a crossing an undo took out (UI thread).</param>
    public CrossingUndoJournal(
        DesignCanvasViewModel canvas, Action<Component> crossingRestored, Action<Component> crossingDropped)
    {
        _canvas = canvas;
        _crossingRestored = crossingRestored;
        _crossingDropped = crossingDropped;
    }

    /// <summary>Follows <paramref name="commands"/>: attributes changes to the running entry and reverts them on undo.</summary>
    public void Attach(CommandManager commands)
    {
        commands.Executing += OnExecuting;
        commands.Undoing += OnUndoing;
        commands.HistoryEntryRetired += OnRetired;
        commands.HistoryCleared += Clear;
    }

    /// <summary>Records one change of the running re-route (any thread).</summary>
    public void Record(CrossingChange change)
    {
        lock (_sync)
        {
            if (_current.Value is not { } owner || owner.Generation != _historyGeneration
                || !_owners.TryGetValue(owner.Command, out var active) || !ReferenceEquals(owner, active)) return;
            var command = owner.Command;
            // Conversion snapshots include both root and group topology. Replaying these
            // changes against the currently open group's manager would target another scope.
            if (command is RerouteImportedRoutesStateCommand conversion)
            {
                conversion.RecordCrossingChange(change);
                return;
            }
            if (!_changes.TryGetValue(command, out var list))
                _changes[command] = list = new List<CrossingChange>();
            list.Add(change);
        }
    }

    /// <summary>Number of changes attributed to <paramref name="command"/> (diagnostics and tests).</summary>
    public int ChangeCount(IUndoableCommand command)
    {
        lock (_sync)
            return _changes.TryGetValue(command, out var list) ? list.Count : 0;
    }

    private void OnExecuting(IUndoableCommand command)
    {
        lock (_sync)
        {
            if (!_owners.TryGetValue(command, out var owner))
                _owners[command] = owner = new(command, _historyGeneration);
            _current.Value = owner;
            // A merged edit keeps the order from before its first part.
            if (command is RerouteImportedRoutesStateCommand) return;
            if (!_changes.ContainsKey(command) && !_orders.ContainsKey(command))
                _orders[command] = CrossingCanvasSync.SnapshotConnections(_canvas);
        }
    }

    /// <summary>Connection order before each edit — routing order decides results, so undo restores it.</summary>
    private readonly Dictionary<IUndoableCommand, List<CAP_Core.Components.Connections.WaveguideConnection>> _orders = new(ReferenceEqualityComparer.Instance);

    private void OnRetired(IUndoableCommand command)
    {
        lock (_sync)
        {
            _changes.Remove(command);
            _orders.Remove(command);
            _owners.Remove(command);
            if (ReferenceEquals(_current.Value?.Command, command)) _current.Value = null;
        }
    }

    private void OnUndoing(IUndoableCommand command)
    {
        List<CrossingChange>? changes;
        List<CAP_Core.Components.Connections.WaveguideConnection>? order;
        lock (_sync)
        {
            _current.Value = null;
            _owners.Remove(command);
            _changes.Remove(command, out changes);
            _orders.Remove(command, out order);
        }
        var manager = _canvas.ConnectionManager;
        if (changes != null)
            Revert(changes, manager);
        if (order != null)
            manager.RestoreConnectionOrder(order);
    }

    /// <summary>Reverts the changes newest first and syncs the canvas.</summary>
    private void Revert(List<CrossingChange> changes, CAP_Core.Components.Connections.WaveguideConnectionManager manager)
    {
        for (int i = changes.Count - 1; i >= 0; i--)
        {
            var change = changes[i];
            if (!change.Undo(manager)) continue;
            foreach (var crossing in change.Added)
                _crossingDropped(crossing);
            foreach (var crossing in change.Removed)
                _crossingRestored(crossing);
        }
        CrossingCanvasSync.SyncConnections(_canvas, CrossingCanvasSync.SnapshotConnections(_canvas));
    }

    private void Clear()
    {
        lock (_sync)
        {
            // A finishing worker retains its execution context; it must not recreate
            // discarded history after a load or history reset.
            _historyGeneration++;
            _changes.Clear();
            _orders.Clear();
            _owners.Clear();
            _current.Value = null;
        }
    }
}
