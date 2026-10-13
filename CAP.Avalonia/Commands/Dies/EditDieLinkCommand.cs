using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// Edits a die-to-die link: a new facet gap moves the far die along the link axis (the gap
/// is geometry, not a number on the side), a new medium index changes what fills the gap.
/// One undo step restores both.
/// </summary>
public sealed class EditDieLinkCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly WaveguideConnection _link;
    private readonly MoveComponentCommand? _move;
    private readonly double _oldIndex;
    private readonly double _newIndex;

    private EditDieLinkCommand(DesignCanvasViewModel canvas, WaveguideConnection link, MoveComponentCommand? move, double newIndex)
    {
        _canvas = canvas;
        _link = link;
        _move = move;
        _oldIndex = link.FacetGapMediumIndex;
        _newIndex = newIndex;
    }

    /// <summary>
    /// The edit setting <paramref name="link"/>'s gap to <paramref name="gapMicrometers"/> and
    /// its medium index to <paramref name="mediumIndex"/>, or null when it is no die-to-die
    /// link or the moved die would overlap another die.
    /// </summary>
    public static EditDieLinkCommand? TryCreate(DesignCanvasViewModel canvas, WaveguideConnection link,
        double gapMicrometers, double mediumIndex)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsCrossChipletFacetLink || !double.IsFinite(gapMicrometers) || gapMicrometers < 0)
            return null;

        var farDie = DieAssembly.DieOf(link.EndPin.ParentComponent);
        var farVm = canvas.Components.FirstOrDefault(c => c.Component == farDie);
        if (farDie == null || farVm == null)
            return null;

        double change = gapMicrometers - DieLinkGeometry.Gap(link);
        double radians = link.StartPin.GetAbsoluteAngle() * Math.PI / 180.0;
        double dx = change * Math.Cos(radians), dy = change * Math.Sin(radians);
        MoveComponentCommand? move = null;
        if (Math.Abs(change) > DieLinkGeometry.GapToleranceMicrometers)
        {
            if (!canvas.CanMoveComponentTo(farVm, farVm.X + dx, farVm.Y + dy))
                return null;
            move = new MoveComponentCommand(canvas, farVm, farVm.X, farVm.Y, farVm.X + dx, farVm.Y + dy);
        }
        return new EditDieLinkCommand(canvas, link, move, mediumIndex);
    }

    /// <inheritdoc/>
    public string Description => "Edit die-to-die link";

    /// <inheritdoc/>
    public void Execute()
    {
        _move?.Execute();
        _link.FacetGapMediumIndex = _newIndex;
        AfterChange();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        _link.FacetGapMediumIndex = _oldIndex;
        _move?.Undo();
        AfterChange();
    }

    private void AfterChange()
    {
        _canvas.Dies.ApplyScopeBounds(reinitializeRouting: false);
        _canvas.InvalidateSimulation();
        _canvas.NotifyDiesChanged();
    }
}

/// <summary>Geometry of a die-to-die link as the coupling model measures it.</summary>
public static class DieLinkGeometry
{
    /// <summary>Gap changes below this are treated as no change.</summary>
    public const double GapToleranceMicrometers = 1e-6;

    /// <summary>The facet-to-facet gap along the start facet's axis (0 when overlapping).</summary>
    public static double Gap(WaveguideConnection link)
    {
        var (sx, sy) = link.StartPin.GetAbsolutePosition();
        var (ex, ey) = link.EndPin.GetAbsolutePosition();
        double radians = link.StartPin.GetAbsoluteAngle() * Math.PI / 180.0;
        return Math.Max(0.0, (ex - sx) * Math.Cos(radians) + (ey - sy) * Math.Sin(radians));
    }

    /// <summary>The facet offset perpendicular to the link axis.</summary>
    public static double LateralOffset(WaveguideConnection link)
    {
        var (sx, sy) = link.StartPin.GetAbsolutePosition();
        var (ex, ey) = link.EndPin.GetAbsolutePosition();
        double radians = link.StartPin.GetAbsoluteAngle() * Math.PI / 180.0;
        return Math.Abs((ex - sx) * Math.Sin(radians) - (ey - sy) * Math.Cos(radians));
    }
}
