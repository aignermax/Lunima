using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for moving a component on the canvas.
/// Supports merging multiple small moves into one command.
/// Uses the Core Component reference instead of ComponentViewModel to survive delete/undo cycles.
/// </summary>
public class MoveComponentCommand : IMergeableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Component _component;
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
        _component = componentViewModel.Component;
        _startX = startX;
        _startY = startY;
        _endX = endX;
        _endY = endY;
        _lastMoveTime = DateTime.Now;
    }

    public string Description => $"Move {_component.Identifier}";

    public void Execute()
    {
        // Don't move locked components
        if (_component.IsLocked)
            return;

        // Find the current ComponentViewModel for this component
        var componentViewModel = _canvas.Components.FirstOrDefault(c => c.Component == _component);
        if (componentViewModel == null)
            return; // Component no longer exists in canvas

        try
        {
            _canvas.BeginCommandExecution();

            // Move to end position
            var deltaX = _endX - componentViewModel.X;
            var deltaY = _endY - componentViewModel.Y;

            if (Math.Abs(deltaX) > 0.001 || Math.Abs(deltaY) > 0.001)
            {
                _canvas.MoveComponent(componentViewModel, deltaX, deltaY);
            }
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
    }

    public void Undo()
    {
        // Find the current ComponentViewModel for this component
        var componentViewModel = _canvas.Components.FirstOrDefault(c => c.Component == _component);
        if (componentViewModel == null)
            return; // Component no longer exists in canvas

        try
        {
            _canvas.BeginCommandExecution();

            // Move back to start position
            var deltaX = _startX - componentViewModel.X;
            var deltaY = _startY - componentViewModel.Y;
            _canvas.MoveComponent(componentViewModel, deltaX, deltaY);
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
    }

    public bool CanMergeWith(IUndoableCommand other)
    {
        if (other is not MoveComponentCommand otherMove)
            return false;

        // Only merge if it's the same component and within the time window
        return otherMove._component == _component &&
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
