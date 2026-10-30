using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Panels.Interaction;

/// <summary>Owns drag start positions and records the completed gesture as one undo operation.</summary>
internal sealed class CanvasMoveGesture
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private double _moveStartX;
    private double _moveStartY;
    private ComponentViewModel? _movingComponent;
    private Dictionary<ComponentViewModel, (double x, double y)>? _groupMoveStartPositions;

    public CanvasMoveGesture(DesignCanvasViewModel canvas, CommandManager commands)
    {
        _canvas = canvas;
        _commandManager = commands;
    }

    public void Reset()
    {
        if (_movingComponent != null || _groupMoveStartPositions != null)
            _canvas.AbandonDrag();
        _movingComponent = null;
        _groupMoveStartPositions = null;
    }

    /// <summary>
    /// Starts dragging a component.
    /// </summary>
    public void StartMoveComponent(ComponentViewModel component)
    {
        _movingComponent = component;
        _moveStartX = component.X;
        _moveStartY = component.Y;
        _canvas.BeginDragComponent(component);
    }

    /// <summary>
    /// Starts dragging multiple components as a group.
    /// </summary>
    public void StartGroupMove(IEnumerable<ComponentViewModel> components)
    {
        _groupMoveStartPositions = new Dictionary<ComponentViewModel, (double x, double y)>();
        foreach (var comp in components)
        {
            _groupMoveStartPositions[comp] = (comp.X, comp.Y);
        }

        var firstComp = components.FirstOrDefault();
        if (firstComp != null)
        {
            _canvas.BeginDragComponent(firstComp);
        }
    }

    /// <summary>
    /// Ends dragging a component and creates undo command.
    /// </summary>
    public void EndMoveComponent()
    {
        if (_movingComponent != null)
        {
            if (Math.Abs(_movingComponent.X - _moveStartX) > 0.001 ||
                Math.Abs(_movingComponent.Y - _moveStartY) > 0.001)
            {
                var cmd = new MoveComponentCommand(
                    _canvas,
                    _movingComponent,
                    _moveStartX,
                    _moveStartY,
                    _movingComponent.X,
                    _movingComponent.Y);
                _commandManager.ExecuteCommand(cmd);
            }
            _canvas.EndDragComponent(_movingComponent);
        }
        _movingComponent = null;
    }

    /// <summary>
    /// Ends dragging multiple components and creates undo command.
    /// </summary>
    public void EndGroupMove(IEnumerable<ComponentViewModel> components)
    {
        if (_groupMoveStartPositions == null || !_groupMoveStartPositions.Any())
            return;

        var firstComp = _groupMoveStartPositions.Keys.FirstOrDefault();
        if (firstComp == null)
            return;

        var startPos = _groupMoveStartPositions[firstComp];
        double deltaX = firstComp.X - startPos.x;
        double deltaY = firstComp.Y - startPos.y;

        if (Math.Abs(deltaX) > 0.001 || Math.Abs(deltaY) > 0.001)
        {
            var cmd = new GroupMoveCommand(
                _canvas,
                _groupMoveStartPositions.Keys.ToList(),
                deltaX,
                deltaY);
            _commandManager.ExecuteCommand(cmd);
        }

        _canvas.EndDragComponent(firstComp);
        _groupMoveStartPositions = null;
    }

}
