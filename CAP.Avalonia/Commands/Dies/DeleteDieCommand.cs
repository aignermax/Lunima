using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// Deletes a die with everything on it and its links (one undo step), then re-bounds the
/// canvas: removing the last die returns the design to the single chip.
/// </summary>
public sealed class DeleteDieCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly DeleteComponentCommand _delete;

    /// <summary>Creates the command for the die shown by <paramref name="die"/>.</summary>
    public DeleteDieCommand(DesignCanvasViewModel canvas, ComponentViewModel die)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _delete = new DeleteComponentCommand(canvas, die ?? throw new ArgumentNullException(nameof(die)));
        Description = $"Delete die {die.Name}";
    }

    /// <inheritdoc/>
    public string Description { get; }

    /// <inheritdoc/>
    public void Execute()
    {
        _delete.Execute();
        Refresh();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        _delete.Undo();
        _canvas.RefreshDieInterfaces();
        Refresh();
    }

    private void Refresh()
    {
        _canvas.Dies.ApplyScopeBounds();
        _canvas.NotifyDiesChanged();
    }
}
