namespace CAP.Avalonia.Commands;

/// <summary>
/// Several commands recorded as one undo step: executed in order, undone in reverse order,
/// so one Ctrl+Z reverts an edit that is built from existing commands (e.g. a cut crossing
/// plus the wire re-docked through it).
/// </summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _steps;

    /// <summary>Creates the composite.</summary>
    /// <param name="description">Undo-history label.</param>
    /// <param name="steps">The commands, in execution order.</param>
    public CompositeCommand(string description, IReadOnlyList<IUndoableCommand> steps)
    {
        Description = description;
        _steps = steps;
    }

    /// <inheritdoc/>
    public string Description { get; }

    /// <inheritdoc/>
    public void Execute()
    {
        foreach (var step in _steps)
            step.Execute();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        for (var i = _steps.Count - 1; i >= 0; i--)
            _steps[i].Undo();
    }
}
