using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Adds canvas-level pin-less frozen paths in one undoable step — the leftover routing
/// and background geometry of a flat GDS import, so undoing the import removes it too.
/// Undo/redo keep the same view-model instances.
/// </summary>
public class AddCanvasFrozenPathsCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly IReadOnlyList<CanvasFrozenPathViewModel> _paths;

    /// <summary>Creates the command for <paramref name="paths"/>.</summary>
    public AddCanvasFrozenPathsCommand(DesignCanvasViewModel canvas, IReadOnlyList<CanvasFrozenPathViewModel> paths)
    {
        _canvas = canvas;
        _paths = paths;
    }

    /// <inheritdoc />
    public string Description => $"Add {_paths.Count} imported geometry path(s)";

    /// <inheritdoc />
    public void Execute()
    {
        foreach (var path in _paths)
        {
            if (!_canvas.CanvasFrozenPaths.Contains(path))
                _canvas.CanvasFrozenPaths.Add(path);
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (var path in _paths)
        {
            path.IsSelected = false;
            _canvas.CanvasFrozenPaths.Remove(path);
        }
    }
}
