using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for moving a component on the canvas.
/// Supports merging multiple small moves into one command.
/// </summary>
public class MoveComponentCommand : IMergeableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentViewModel _componentViewModel;
    private readonly double _startX;
    private readonly double _startY;
    private double _endX;
    private double _endY;

    /// <summary>
    /// Time window in milliseconds for merging move commands.
    /// </summary>
    private static readonly TimeSpan MergeTimeWindow = TimeSpan.FromMilliseconds(500);
    private DateTime _lastMoveTime;

    public MoveComponentCommand(
        DesignCanvasViewModel canvas,
        ComponentViewModel componentViewModel,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        _canvas = canvas;
        _componentViewModel = componentViewModel;
        _startX = startX;
        _startY = startY;
        _endX = endX;
        _endY = endY;
        _lastMoveTime = DateTime.Now;
    }

    public string Description => $"Move {_componentViewModel.Component.Identifier}";

    public void Execute()
    {
        // Move to end position
        var deltaX = _endX - _componentViewModel.X;
        var deltaY = _endY - _componentViewModel.Y;

        if (Math.Abs(deltaX) > 0.001 || Math.Abs(deltaY) > 0.001)
        {
            _canvas.MoveComponent(_componentViewModel, deltaX, deltaY);
        }
    }

    public void Undo()
    {
        // Move back to start position
        var deltaX = _startX - _componentViewModel.X;
        var deltaY = _startY - _componentViewModel.Y;
        _canvas.MoveComponent(_componentViewModel, deltaX, deltaY);
    }

    public bool CanMergeWith(IUndoableCommand other)
    {
        if (other is not MoveComponentCommand otherMove)
            return false;

        // Only merge if it's the same component and within the time window
        return otherMove._componentViewModel == _componentViewModel &&
               DateTime.Now - _lastMoveTime < MergeTimeWindow;
    }

    public void MergeWith(IUndoableCommand other)
    {
        if (other is MoveComponentCommand otherMove)
        {
            // Keep start position, update end position
            _endX = otherMove._endX;
            _endY = otherMove._endY;
            _lastMoveTime = DateTime.Now;
        }
    }
}
