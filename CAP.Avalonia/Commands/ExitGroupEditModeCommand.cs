using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Undoable command for exiting ComponentGroup edit mode.
/// Allows undo to re-enter edit mode and redo to exit again.
/// </summary>
public class ExitGroupEditModeCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _group;

    /// <summary>
    /// Creates a command to exit the current group edit mode.
    /// </summary>
    /// <param name="canvas">The canvas view model managing edit mode state.</param>
    /// <param name="group">The group being exited (captured for undo re-entry).</param>
    public ExitGroupEditModeCommand(DesignCanvasViewModel canvas, ComponentGroup group)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _group = group ?? throw new ArgumentNullException(nameof(group));
    }

    /// <inheritdoc />
    public string Description => $"Exit group edit mode: {_group.GroupName}";

    /// <inheritdoc />
    public void Execute()
    {
        _canvas.ExitGroupEditMode();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _canvas.EnterGroupEditMode(_group);
    }
}
