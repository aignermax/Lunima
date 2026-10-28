using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Manages command history for undo/redo functionality.
/// </summary>
public class CommandManager : INotifyPropertyChanged
{
    private const int MaxHistorySize = 100;

    private readonly LinkedList<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    private readonly Dictionary<IUndoableCommand, LinkedListNode<IUndoableCommand>?> _pending = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<IUndoableCommand, object> _retiredReservations = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when the canvas state changes due to command execution, undo, or redo.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised before a command runs (execute or redo), with the history entry it belongs to —
    /// the earlier command it merges into, if any. Side effects that follow (e.g. what the
    /// re-route after the edit changes) are attributed to that entry.
    /// </summary>
    public event Action<IUndoableCommand>? Executing;

    /// <summary>Raised before a command is undone, so its attributed side effects can be reverted first.</summary>
    public event Action<IUndoableCommand>? Undoing;

    /// <summary>Raised after the whole history was dropped.</summary>
    public event Action? HistoryCleared;

    /// <summary>Raised when an entry can no longer be undone, redone, or committed.</summary>
    public event Action<IUndoableCommand>? HistoryEntryRetired;

    /// <summary>
    /// Gets whether there are commands that can be undone.
    /// </summary>
    public bool CanUndo => _pending.Count == 0 && _undoStack.Count > 0;

    /// <summary>
    /// Gets whether there are commands that can be redone.
    /// </summary>
    public bool CanRedo => _pending.Count == 0 && _redoStack.Count > 0;

    /// <summary>
    /// Gets the description of the next command to undo.
    /// </summary>
    public string? UndoDescription => _undoStack.Last?.Value.Description;

    /// <summary>
    /// Gets the description of the next command to redo.
    /// </summary>
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    /// <summary>
    /// Gets the number of commands in the undo history.
    /// </summary>
    public int UndoCount => _undoStack.Count;

    /// <summary>
    /// Gets the number of commands in the redo stack.
    /// </summary>
    public int RedoCount => _redoStack.Count;

    /// <summary>Establishes ownership before an asynchronous command starts mutating the design.</summary>
    public void BeginExecution(IUndoableCommand command)
    {
        if (_retiredReservations.TryGetValue(command, out _)) return;
        _pending.Add(command, _undoStack.Last);
        Executing?.Invoke(command);
        NotifyPropertyChanged(nameof(CanUndo));
        NotifyPropertyChanged(nameof(CanRedo));
    }

    /// <summary>Releases an abandoned asynchronous reservation without changing history.</summary>
    public void CancelExecution(IUndoableCommand command)
    {
        if (!_pending.Remove(command)) return;
        _retiredReservations.GetValue(command, _ => new object());
        HistoryEntryRetired?.Invoke(command);
        NotifyPropertyChanged(nameof(CanUndo));
        NotifyPropertyChanged(nameof(CanRedo));
    }

    /// <summary>
    /// Executes a command and adds it to the history.
    /// </summary>
    public void ExecuteCommand(IUndoableCommand command)
    {
        if (_retiredReservations.TryGetValue(command, out _))
        {
            NotifyPropertyChanged(nameof(CanUndo));
            NotifyPropertyChanged(nameof(CanRedo));
            return;
        }
        var completing = _pending.Remove(command, out var anchor);
        // Decided up front so side effects of the execution are attributed to the history
        // entry that will own them.
        var mergeTarget = !completing && _pending.Count == 0 && _undoStack.Count > 0
                          && _undoStack.Last!.Value is IMergeableCommand candidate
                          && candidate.CanMergeWith(command)
            ? candidate
            : null;
        Executing?.Invoke(mergeTarget ?? command);
        command.Execute();

        // Try to merge with the last command
        if (mergeTarget is { } lastMergeable)
        {
            lastMergeable.MergeWith(command);
        }
        else
        {
            if (completing && anchor?.List == _undoStack)
                _undoStack.AddAfter(anchor, command);
            else if (completing)
                _undoStack.AddFirst(command);
            else
                _undoStack.AddLast(command);

            // Trim history if too large
            while (_undoStack.Count > MaxHistorySize)
            {
                var retired = _undoStack.First!.Value;
                _undoStack.RemoveFirst();
                HistoryEntryRetired?.Invoke(retired);
            }
        }

        // Clear redo stack when new command is executed
        var discardedRedo = _redoStack.ToArray();
        _redoStack.Clear();
        foreach (var retired in discardedRedo) HistoryEntryRetired?.Invoke(retired);

        NotifyPropertyChanged(nameof(CanUndo));
        NotifyPropertyChanged(nameof(CanRedo));
        NotifyPropertyChanged(nameof(UndoDescription));
        NotifyPropertyChanged(nameof(RedoDescription));
        NotifyPropertyChanged(nameof(UndoCount));
        NotifyPropertyChanged(nameof(RedoCount));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Undoes the last command.
    /// </summary>
    /// <returns>True if a command was undone, false if there was nothing to undo.</returns>
    public bool Undo()
    {
        if (!CanUndo)
            return false;

        var command = _undoStack.Last!.Value;
        _undoStack.RemoveLast();

        Undoing?.Invoke(command);
        command.Undo();
        _redoStack.Push(command);

        NotifyPropertyChanged(nameof(CanUndo));
        NotifyPropertyChanged(nameof(CanRedo));
        NotifyPropertyChanged(nameof(UndoDescription));
        NotifyPropertyChanged(nameof(RedoDescription));
        NotifyPropertyChanged(nameof(UndoCount));
        NotifyPropertyChanged(nameof(RedoCount));
        StateChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Redoes the last undone command.
    /// </summary>
    /// <returns>True if a command was redone, false if there was nothing to redo.</returns>
    public bool Redo()
    {
        if (!CanRedo)
            return false;

        var command = _redoStack.Pop();
        Executing?.Invoke(command);
        command.Execute();
        _undoStack.AddLast(command);

        NotifyPropertyChanged(nameof(CanUndo));
        NotifyPropertyChanged(nameof(CanRedo));
        NotifyPropertyChanged(nameof(UndoDescription));
        NotifyPropertyChanged(nameof(RedoDescription));
        NotifyPropertyChanged(nameof(UndoCount));
        NotifyPropertyChanged(nameof(RedoCount));
        StateChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Clears all command history.
    /// </summary>
    public void ClearHistory()
    {
        var retired = _undoStack.Concat(_redoStack).Concat(_pending.Keys).ToArray();
        foreach (var command in _pending.Keys)
            _retiredReservations.GetValue(command, _ => new object());
        _pending.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        foreach (var command in retired) HistoryEntryRetired?.Invoke(command);
        HistoryCleared?.Invoke();

        NotifyPropertyChanged(nameof(CanUndo));
        NotifyPropertyChanged(nameof(CanRedo));
        NotifyPropertyChanged(nameof(UndoDescription));
        NotifyPropertyChanged(nameof(RedoDescription));
        NotifyPropertyChanged(nameof(UndoCount));
        NotifyPropertyChanged(nameof(RedoCount));
    }

    private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Raises the StateChanged event to notify listeners that the canvas state has changed.
    /// </summary>
    public void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
