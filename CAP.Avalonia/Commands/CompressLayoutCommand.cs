using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for compressing the layout (minimize chip area).
/// Supports undo/redo by storing original positions.
/// </summary>
public class CompressLayoutCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Dictionary<Component, (double X, double Y)> _originalPositions;
    private readonly Dictionary<Component, (double X, double Y)> _newPositions;

    public CompressLayoutCommand(
        DesignCanvasViewModel canvas,
        Dictionary<Component, (double X, double Y)> originalPositions,
        Dictionary<Component, (double X, double Y)> newPositions)
    {
        _canvas = canvas;
        _originalPositions = originalPositions;
        _newPositions = newPositions;
    }

    public string Description => "Compress Layout";

    public void Execute()
    {
        ApplyPositions(_newPositions);
    }

    public void Undo()
    {
        ApplyPositions(_originalPositions);
    }

    private void ApplyPositions(Dictionary<Component, (double X, double Y)> positions)
    {
        try
        {
            _canvas.BeginCommandExecution();

            foreach (var kvp in positions)
            {
                var component = kvp.Key;
                var (x, y) = kvp.Value;

                // Find the ComponentViewModel for this component
                var componentViewModel = _canvas.Components
                    .FirstOrDefault(c => c.Component == component);

                if (componentViewModel != null)
                {
                    componentViewModel.X = x;
                    componentViewModel.Y = y;
                    component.PhysicalX = x;
                    component.PhysicalY = y;
                }
            }
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
    }
}
