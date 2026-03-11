using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for moving multiple components as a single undoable operation.
/// All components move by the same delta.
/// </summary>
public class GroupMoveCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly List<ComponentViewModel> _components;
    private readonly double _deltaX;
    private readonly double _deltaY;
    private bool _isFirstExecution = true;

    /// <summary>
    /// Creates a group move command.
    /// </summary>
    /// <param name="canvas">The canvas ViewModel.</param>
    /// <param name="components">Components to move.</param>
    /// <param name="deltaX">Horizontal movement in micrometers.</param>
    /// <param name="deltaY">Vertical movement in micrometers.</param>
    public GroupMoveCommand(
        DesignCanvasViewModel canvas,
        IReadOnlyList<ComponentViewModel> components,
        double deltaX,
        double deltaY)
    {
        _canvas = canvas;
        _components = components.ToList();
        _deltaX = deltaX;
        _deltaY = deltaY;
    }

    /// <inheritdoc />
    public string Description => $"Move {_components.Count} components";

    /// <inheritdoc />
    public void Execute()
    {
        // On first execution, components are already at their final position from the drag operation.
        // Only execute the move on Redo (when _isFirstExecution is false).
        if (_isFirstExecution)
        {
            _isFirstExecution = false;
            return;
        }

        try
        {
            _canvas.BeginCommandExecution();
            MoveAll(_deltaX, _deltaY);
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        try
        {
            _canvas.BeginCommandExecution();
            MoveAll(-_deltaX, -_deltaY);
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
    }

    private void MoveAll(double dx, double dy)
    {
        foreach (var comp in _components)
        {
            // Skip locked components
            if (!comp.Component.IsLocked)
            {
                _canvas.MoveComponent(comp, dx, dy);
            }
        }
    }
}
