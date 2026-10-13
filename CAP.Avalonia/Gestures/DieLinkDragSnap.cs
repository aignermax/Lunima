using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Gestures;

/// <summary>
/// Die-to-die snapping while a die is dragged on the assembly: the die follows the pointer
/// until one of its edge-coupler facets comes near a facing facet of another die, then
/// locks on axis at the link gap (<see cref="DieLinkSnapper"/>). On release the aligned
/// pairs are linked in one undo step. Alt drags freely.
/// </summary>
public sealed class DieLinkDragSnap
{
    private double _rawX;
    private double _rawY;
    private bool _active;

    /// <summary>The snap currently shown (guide + loss label), or null.</summary>
    public DieLinkSnap? Current { get; private set; }

    /// <summary>Starts tracking a drag of <paramref name="dragged"/>.</summary>
    public void Begin(DesignCanvasViewModel canvas, ComponentViewModel dragged)
    {
        _rawX = dragged.X;
        _rawY = dragged.Y;
        _active = dragged.Component is ComponentGroup { IsDie: true } && canvas.Dies.IsAssemblyRoot;
        Current = null;
    }

    /// <summary>
    /// Moves the dragged die by the pointer delta, snapped onto a neighbour when in range.
    /// Returns false (caller moves normally) when the drag is not a die drag on the assembly.
    /// </summary>
    public bool TryMove(DesignCanvasViewModel canvas, ComponentViewModel dragged, double dx, double dy, bool suppressSnap)
    {
        if (!_active)
            return false;
        _rawX += dx;
        _rawY += dy;
        var die = (ComponentGroup)dragged.Component;
        double rawDx = _rawX - dragged.X, rawDy = _rawY - dragged.Y;
        Current = suppressSnap ? null : DieLinkSnapper.Plan(die, rawDx, rawDy, canvas.Dies.RootDies);
        var (moveX, moveY) = Current is { } snap ? (snap.DeltaX, snap.DeltaY) : (rawDx, rawDy);
        canvas.MoveComponent(dragged, moveX, moveY);
        return true;
    }

    /// <summary>True while a snap holds the die in place (release keeps the snapped position exactly).</summary>
    public bool IsSnapped => Current is { Pairs.Count: > 0 };

    /// <summary>Ends the drag; links the aligned pairs when the drop was accepted.</summary>
    public void End(DesignCanvasViewModel canvas, MainViewModel? mainVm, bool dropAccepted)
    {
        var snap = Current;
        Current = null;
        _active = false;
        if (!dropAccepted || snap == null || mainVm == null)
            return;
        var link = LinkDiesCommand.TryCreate(canvas, snap.Pairs);
        if (link == null)
            return;
        mainVm.CommandManager.ExecuteCommand(link);
        mainVm.StatusText = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            Services.Localization.LocalizationService.Instance.Translate("Dies.Status.Linked"), link.LinkCount);
    }
}
