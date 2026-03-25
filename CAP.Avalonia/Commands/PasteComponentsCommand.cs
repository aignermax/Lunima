using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for pasting copied components onto the canvas.
/// On first Execute, creates new clones via clipboard. On Redo, re-adds
/// the same component instances to avoid creating shadow/duplicate components.
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
        // Redo scenario: re-add the SAME components instead of creating new clones
        if (_result != null)
        {
            ReAddPastedComponents();
            return;
        }

        // First execution: create new clones via clipboard
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

        // Remove pasted components (but keep _result so Redo can restore them)
        foreach (var comp in _result.Components)
        {
            _canvas.RemoveComponent(comp);
        }
    }

    /// <summary>
    /// Re-adds previously pasted components on Redo, preserving object identity.
    /// </summary>
    private void ReAddPastedComponents()
    {
        try
        {
            _canvas.BeginCommandExecution();

            foreach (var compVm in _result!.Components)
            {
                _canvas.Components.Add(compVm);
                _canvas.Router.AddComponentObstacle(compVm.Component);

                foreach (var pin in compVm.Component.PhysicalPins)
                {
                    _canvas.AllPins.Add(new PinViewModel(pin, compVm));
                }
            }

            // Re-add connections
            foreach (var connVm in _result.Connections)
            {
                _canvas.ConnectionManager.AddExistingConnection(connVm.Connection);
                _canvas.Connections.Add(connVm);
            }
        }
        finally
        {
            _canvas.EndCommandExecution();
        }

        _ = _canvas.RecalculateRoutesAsync();
        _canvas.InvalidateSimulation();
    }

    /// <summary>
    /// Gets the pasted components (available after Execute).
    /// </summary>
    public PasteResult? Result => _result;
}
