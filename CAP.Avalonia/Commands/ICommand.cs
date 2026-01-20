namespace CAP.Avalonia.Commands;

/// <summary>
/// Interface for undoable commands in the application.
/// </summary>
public interface IUndoableCommand
{
    /// <summary>
    /// Executes the command.
    /// </summary>
    void Execute();

    /// <summary>
    /// Undoes the command, reverting to the state before execution.
    /// </summary>
    void Undo();

    /// <summary>
    /// Gets a description of the command for display purposes.
    /// </summary>
    string Description { get; }
}

/// <summary>
/// Interface for commands that can be merged with similar commands.
/// For example, multiple small move operations can be merged into one.
/// </summary>
public interface IMergeableCommand : IUndoableCommand
{
    /// <summary>
    /// Determines if this command can be merged with another command.
    /// </summary>
    bool CanMergeWith(IUndoableCommand other);

    /// <summary>
    /// Merges another command into this one.
    /// </summary>
    void MergeWith(IUndoableCommand other);
}
