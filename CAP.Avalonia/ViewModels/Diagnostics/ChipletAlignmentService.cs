using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// Applies the one-click "Align chiplet" fix (issue #1248, rung 6) on the canvas:
/// plans the translation with <see cref="ChipletLinkAligner"/> and, when planning
/// succeeds, moves the end pin's chiplet group through the same undoable
/// <see cref="GroupMoveCommand"/> a drag-drop records, so Ctrl+Z restores the
/// original position exactly.
/// </summary>
public class ChipletAlignmentService
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly ChipletLinkAligner _aligner = new();

    /// <summary>
    /// Creates the service bound to a canvas and its undo history.
    /// </summary>
    public ChipletAlignmentService(DesignCanvasViewModel canvas, CommandManager commandManager)
    {
        _canvas = canvas;
        _commandManager = commandManager;
    }

    /// <summary>
    /// Attempts to snap <paramref name="connection"/>'s end chiplet into butt-coupling.
    /// Returns null on success (the chiplet moved, undoably) or the refusal reason —
    /// in which case nothing moved.
    /// </summary>
    public ChipletAlignmentRefusal? TryAlign(WaveguideConnection connection, double wavelengthNm)
    {
        var connections = _canvas.Connections.Select(c => c.Connection).ToList();
        var components = _canvas.Components.Select(c => c.Component).ToList();
        if (!_aligner.TryPlan(connection, connections, components, wavelengthNm, out var plan, out var refusal))
            return refusal;

        var groupViewModel = _canvas.Components.FirstOrDefault(c => c.Component == plan!.Chiplet);
        if (groupViewModel == null)
            return ChipletAlignmentRefusal.NotCrossChipletLink; // group no longer on the canvas

        // Same choreography as a drag-drop: BeginDrag suppresses the mid-drag re-route
        // and the placement gate (the aligner already ran its own overlap check),
        // MoveComponent performs the group move, the command records it for undo (its
        // first Execute is a no-op because the move already happened), and EndDrag
        // re-routes the affected wires.
        _canvas.BeginDragComponent(groupViewModel);
        _canvas.MoveComponent(groupViewModel, plan!.DeltaX, plan.DeltaY);
        _commandManager.ExecuteCommand(
            new GroupMoveCommand(_canvas, new[] { groupViewModel }, plan.DeltaX, plan.DeltaY));
        _canvas.EndDragComponent(groupViewModel);
        return null;
    }
}
