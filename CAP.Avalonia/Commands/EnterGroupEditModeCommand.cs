using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Undoable command for entering ComponentGroup edit mode.
/// Allows undo to exit edit mode and redo to re-enter it.
/// </summary>
public class EnterGroupEditModeCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _group;

    /// <summary>
    /// Creates a command to enter edit mode for the specified group.
    /// </summary>
    /// <param name="canvas">The canvas view model managing edit mode state.</param>
    /// <param name="group">The group to enter edit mode for.</param>
    public EnterGroupEditModeCommand(DesignCanvasViewModel canvas, ComponentGroup group)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _group = group ?? throw new ArgumentNullException(nameof(group));
    }

    /// <inheritdoc />
    public string Description => $"Enter group edit mode: {_group.GroupName}";

    /// <inheritdoc />
    public void Execute()
    {
        _canvas.EnterGroupEditMode(_group);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _canvas.ExitGroupEditMode();
    }
}
