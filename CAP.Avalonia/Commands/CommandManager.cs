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

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when the canvas state changes due to command execution, undo, or redo.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets whether there are commands that can be undone.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets whether there are commands that can be redone.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

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

    /// <summary>
    /// Executes a command and adds it to the history.
    /// </summary>
    public void ExecuteCommand(IUndoableCommand command)
    {
        command.Execute();

        // Try to merge with the last command
        if (_undoStack.Count > 0 &&
            _undoStack.Last!.Value is IMergeableCommand lastMergeable &&
            lastMergeable.CanMergeWith(command))
        {
            lastMergeable.MergeWith(command);
        }
        else
        {
            _undoStack.AddLast(command);

            // Trim history if too large
            while (_undoStack.Count > MaxHistorySize)
            {
                _undoStack.RemoveFirst();
            }
        }

        // Clear redo stack when new command is executed
        _redoStack.Clear();

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
        if (_undoStack.Count == 0)
            return false;

        var command = _undoStack.Last!.Value;
        _undoStack.RemoveLast();

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
        if (_redoStack.Count == 0)
            return false;

        var command = _redoStack.Pop();
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
        _undoStack.Clear();
        _redoStack.Clear();

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
