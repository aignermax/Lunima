using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for pasting copied components onto the canvas.
/// Supports undo by removing all pasted components.
/// </summary>
public class PasteComponentsCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentClipboard _clipboard;
    private readonly double? _targetX;
    private readonly double? _targetY;
    private PasteResult? _result;

    /// <summary>
    /// Creates a paste command.
    /// </summary>
    /// <param name="targetX">Optional target X position (canvas coordinates)</param>
    /// <param name="targetY">Optional target Y position (canvas coordinates)</param>
    public PasteComponentsCommand(
        DesignCanvasViewModel canvas,
        ComponentClipboard clipboard,
        double? targetX = null,
        double? targetY = null)
    {
        _canvas = canvas;
        _clipboard = clipboard;
        _targetX = targetX;
        _targetY = targetY;
    }

    /// <inheritdoc />
    public string Description => "Paste components";

    /// <inheritdoc />
    public void Execute()
    {
        _result = _clipboard.Paste(_canvas, _targetX, _targetY);
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_result == null) return;

        // Remove pasted connections first
        foreach (var conn in _result.Connections)
        {
            _canvas.ConnectionManager.RemoveConnectionDeferred(conn.Connection);
            _canvas.Connections.Remove(conn);
        }

        // Remove pasted components
        foreach (var comp in _result.Components)
        {
            _canvas.RemoveComponent(comp);
        }
    }

    /// <summary>
    /// Gets the pasted components (available after Execute).
    /// </summary>
    public PasteResult? Result => _result;
}
