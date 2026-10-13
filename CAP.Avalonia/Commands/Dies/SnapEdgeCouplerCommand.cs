using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// Puts an edge coupler onto the die edge it was dropped near: turns it so its facet faces
/// out of the die and slides it onto the edge (see <see cref="DieEdgeSnapper"/>). Undo
/// restores the previous pose exactly.
/// </summary>
public sealed class SnapEdgeCouplerCommand : IUndoableCommand
{
    private const int QuarterTurnsPerTurn = 4;

    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentViewModel _coupler;
    private readonly EdgeSnap _snap;
    private readonly double _oldX;
    private readonly double _oldY;

    /// <summary>Creates the command for a planned snap.</summary>
    public SnapEdgeCouplerCommand(DesignCanvasViewModel canvas, ComponentViewModel coupler, EdgeSnap snap)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _coupler = coupler ?? throw new ArgumentNullException(nameof(coupler));
        _snap = snap ?? throw new ArgumentNullException(nameof(snap));
        _oldX = coupler.Component.PhysicalX;
        _oldY = coupler.Component.PhysicalY;
    }

    /// <summary>
    /// Plans a snap for <paramref name="coupler"/> onto the edge of the die the user works
    /// in, or null when it is no edge coupler, no die is active, the coupler is outside the
    /// edge zone, or it already sits on the edge facing out.
    /// </summary>
    public static SnapEdgeCouplerCommand? TryCreate(DesignCanvasViewModel canvas, ComponentViewModel coupler)
    {
        if (canvas.Dies.ActiveDie is not { Die: { } die } activeDie
            || !EdgeCouplerFacet.IsEdgeCoupler(coupler.Component)
            || coupler.Component.IsLocked)
            return null;

        var snap = DieEdgeSnapper.Plan(coupler.Component, DieAssembly.OutlineOf(activeDie), die.EdgeZoneMicrometers);
        if (snap == null || IsAlreadySnapped(coupler.Component, snap))
            return null;
        return new SnapEdgeCouplerCommand(canvas, coupler, snap);
    }

    /// <inheritdoc/>
    public string Description => $"Snap {_coupler.Name} to die edge";

    /// <inheritdoc/>
    public void Execute()
    {
        DieEdgeSnapper.Apply(_coupler.Component, _snap);
        Sync();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        var component = _coupler.Component;
        int undoTurns = (QuarterTurnsPerTurn - _snap.QuarterTurns) % QuarterTurnsPerTurn;
        for (int i = 0; i < undoTurns; i++)
            ComponentPoseTransform.Rotate90CounterClockwise(component);
        component.PhysicalX = _oldX;
        component.PhysicalY = _oldY;
        Sync();
    }

    private static bool IsAlreadySnapped(Component component, EdgeSnap snap) =>
        snap.QuarterTurns == 0
        && Math.Abs(component.PhysicalX - snap.X) < 1e-6
        && Math.Abs(component.PhysicalY - snap.Y) < 1e-6;

    private void Sync()
    {
        _coupler.X = _coupler.Component.PhysicalX;
        _coupler.Y = _coupler.Component.PhysicalY;
        _coupler.NotifyDimensionsChanged();
        _canvas.Router.UpdateComponentObstacle(_coupler.Component);
        _ = _canvas.RecalculateRoutesAsync();
        _canvas.RepaintRequested?.Invoke();
    }
}
