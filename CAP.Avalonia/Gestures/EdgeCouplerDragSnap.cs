using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Gestures;

/// <summary>
/// Edge snapping while dragging an edge coupler inside a die: within the edge zone the drag
/// preview slides along the die edge, and on release the coupler lands there with its facet
/// facing out (one undo step after the move). Alt suppresses snapping.
/// </summary>
public static class EdgeCouplerDragSnap
{
    /// <summary>
    /// The on-edge position to preview for the coupler being dragged, or null when no edge
    /// snap applies (not in a die, not an edge coupler, outside the edge zone, or it would
    /// first have to turn — turning happens on release).
    /// </summary>
    public static (double X, double Y)? PreviewPosition(DesignCanvasViewModel canvas, ComponentViewModel? dragged)
    {
        if (dragged == null || (DieAssembly.DieOf(dragged.Component) ?? canvas.Dies.ActiveDie) is not { Die: { } die } activeDie
            || !EdgeCouplerFacet.IsEdgeCoupler(dragged.Component))
            return null;
        var snap = DieEdgeSnapper.Plan(dragged.Component, DieAssembly.OutlineOf(activeDie), die.EdgeZoneMicrometers);
        return snap is { QuarterTurns: 0 } ? (snap.X, snap.Y) : null;
    }

    /// <summary>Snaps the dropped coupler onto the die edge through an undoable command.</summary>
    public static void SnapAfterDrop(DesignCanvasViewModel canvas, MainViewModel? mainVm, ComponentViewModel? dropped)
    {
        if (dropped == null || mainVm == null)
            return;
        var command = SnapEdgeCouplerCommand.TryCreate(canvas, dropped);
        if (command != null)
            mainVm.CommandManager.ExecuteCommand(command);
    }
}
