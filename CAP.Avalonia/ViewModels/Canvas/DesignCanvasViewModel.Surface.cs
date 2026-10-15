using CAP.Avalonia.ViewModels.Canvas.DieSurface;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Die-surface half of <see cref="DesignCanvasViewModel"/>: parts on the dies of an assembly
/// are worked on in place (see <see cref="DieSurfaceService"/>), and a die moves rigidly —
/// its parts and every wire on it translate unchanged, nothing is re-routed.
/// </summary>
public partial class DesignCanvasViewModel
{
    /// <summary>Parts on the dies of an assembly, worked on in place at the assembly root.</summary>
    public DieSurfaceService Surface { get; private set; } = null!;

    /// <summary>The chip of a single-die design, shown and handled as its one die "Die 1".</summary>
    public ChipDieService ChipDie { get; private set; } = null!;

    private void InitializeDieSurface()
    {
        Surface = new DieSurfaceService(Components, Groups) { ViewModelCreated = CreateSurfaceViewModel };
        ChipDie = new ChipDieService(this);
        Placement.AssemblyPlacementCheck = (x, y, width, height, exclude) =>
            !Surface.IsActive || exclude?.Component is ComponentGroup { IsDie: true }
                ? null
                : Surface.CanPlace(x, y, width, height, exclude?.Component);
    }

    /// <summary>
    /// What the pointer works on: the top-level items, except that at an editable assembly
    /// root the dies' parts take part individually (the dies themselves stay selectable).
    /// </summary>
    public IReadOnlyList<ComponentViewModel> InteractiveComponents =>
        Surface.IsEditable
            ? Components.Where(c => c.Component is not ComponentGroup { IsDie: true }).Concat(Surface.Items).ToList()
            : Components.ToList();

    /// <summary>The view model the canvas uses for <paramref name="component"/>: top-level or on a die.</summary>
    public ComponentViewModel? ViewModelOf(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var topLevel = Components.FirstOrDefault(c => ReferenceEquals(c.Component, component));
        if (topLevel != null)
            return topLevel;
        return component.ParentGroup is ComponentGroup { IsDie: true } ? Surface.ViewModelFor(component) : null;
    }

    /// <summary>
    /// True when the component may sit at (<paramref name="x"/>, <paramref name="y"/>) — for a
    /// part on a die: on its die and clear of the die's other parts.
    /// </summary>
    private bool? CanMoveSurfaceItemTo(ComponentViewModel component, double x, double y)
    {
        if (!Surface.IsItem(component))
            return null;
        var (fx, fy, width, height) = DiePlacement.FootprintOf(component.Component);
        return Surface.CanPlace(fx + x - component.X, fy + y - component.Y, width, height, component.Component);
    }

    /// <summary>
    /// After a move: a die takes its wires along rigidly; a part on a die keeps the die's
    /// boundary pins on the part.
    /// </summary>
    private void AfterMove(ComponentViewModel component, bool moved, double deltaX, double deltaY)
    {
        if (Surface.DieOf(component) is { } die)
        {
            RealignDiePins(die);
            return;
        }
        if (!moved || component.Component is not ComponentGroup { IsDie: true } movedDie)
            return;
        TranslateDieWires(movedDie, deltaX, deltaY);
        RefreshFacetLinkPaths();
        // A drag re-bounds once on release; a command (undo, align, agent) re-bounds right away.
        if (!Placement.IsDragging && Dies.IsAssemblyRoot)
            _ = Dies.ApplyScopeBoundsInBackground();
    }

    /// <summary>
    /// After a wire was stored in (or taken out of) <paramref name="group"/>: the boundary pins
    /// of the group and of everything around it changed, so the canvas pins of the top-level
    /// item holding it — or the dies' interfaces — are rebuilt and the design re-simulated.
    /// </summary>
    public void RefreshGroupInterface(ComponentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var top = group;
        while (top.ParentGroup != null && !ReferenceEquals(top.ParentGroup, CurrentEditGroup))
            top = top.ParentGroup;
        if (!top.IsDie && Components.FirstOrDefault(c => ReferenceEquals(c.Component, top)) is { } vm)
        {
            ResyncGroupPinViewModels(vm, top);
            vm.NotifyDimensionsChanged();
        }
        if (DieAssembly.DieOf(group) != null)
            RefreshDieInterfaces();
        InvalidateSimulation();
        RepaintRequested?.Invoke();
    }

    /// <summary>
    /// Keeps a die's boundary pins on the parts they expose after a part on the die moved or
    /// rotated (position and facing direction).
    /// </summary>
    public void RealignDiePins(ComponentGroup die)
    {
        ArgumentNullException.ThrowIfNull(die);
        foreach (var pin in die.ExternalPins)
        {
            var (x, y) = pin.InternalPin.GetAbsolutePosition();
            pin.RelativeX = x - die.PhysicalX;
            pin.RelativeY = y - die.PhysicalY;
            pin.AngleDegrees = pin.InternalPin.GetAbsoluteAngle();
        }
    }

    /// <summary>
    /// Shifts every wire on <paramref name="die"/> (both ends on the die) by the die's move, so a
    /// die move never re-routes: the routes are identical, only translated.
    /// </summary>
    public void TranslateDieWires(ComponentGroup die, double deltaX, double deltaY)
    {
        foreach (var connectionVm in Connections)
        {
            var connection = connectionVm.Connection;
            if (connection.IsCrossChipletFacetLink || connection.RoutedPath is not { Segments.Count: > 0 } path)
                continue;
            if (!ReferenceEquals(ConnectionOwnership.DieOf(connection.StartPin), die)
                || !ReferenceEquals(ConnectionOwnership.DieOf(connection.EndPin), die))
                continue;
            var asDrawn = connection.AsDrawnGeometry;
            connection.ReplaceRoutedPath(path.TranslatedCopy(deltaX, deltaY));
            if (asDrawn is not null)
                connection.AttachAsDrawnGeometry(asDrawn.Translated(deltaX, deltaY));
            connectionVm.NotifyPathChanged();
        }
    }

    /// <summary>
    /// True when a die move leaves a wire behind that has to be re-routed: only a legacy
    /// waveguide from the die to somewhere else (flagged by the design checks) qualifies.
    /// </summary>
    private bool DieMoveNeedsRouting(ComponentGroup die) =>
        ConnectionManager.Connections.Any(c => !c.IsCrossChipletFacetLink
            && ReferenceEquals(ConnectionOwnership.DieOf(c.StartPin), die)
               != ReferenceEquals(ConnectionOwnership.DieOf(c.EndPin), die));

    /// <summary>
    /// Puts <paramref name="part"/> onto the top level for the duration of a command that
    /// works on top-level items (see <c>DieSurfaceCommand</c>): its view model and pins join the
    /// canvas collections. The caller has taken the part off its die.
    /// </summary>
    public void AttachTopLevel(ComponentViewModel part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (Components.Contains(part))
            return;
        Components.Add(part);
        Router.AddComponentObstacle(part.Component);
        foreach (var pin in part.Component.PhysicalPins)
            AllPins.Add(new PinViewModel(pin, part));
        if (part.Component is ComponentGroup group)
        {
            foreach (var groupPin in group.ExternalPins)
                AllPins.Add(new PinViewModel(groupPin.InternalPin, part));
        }
    }

    /// <summary>
    /// Takes <paramref name="part"/> off the top level again (its pins with it) so it can go
    /// back onto a die; its view model stays the one the die surface uses for the part.
    /// </summary>
    public void DetachTopLevel(ComponentViewModel part)
    {
        ArgumentNullException.ThrowIfNull(part);
        Router.RemoveComponentObstacle(part.Component);
        foreach (var pin in AllPins.Where(p => p.ParentComponentViewModel == part).ToList())
            AllPins.Remove(pin);
        Components.Remove(part);
        Surface.Adopt(part);
    }

    /// <summary>
    /// A part's view model on a die: wired like a top-level part's (slider and laser changes
    /// re-simulate), but not added to the top-level collection — the part stays a child of its die.
    /// </summary>
    private ComponentViewModel CreateSurfaceViewModel(Component part)
    {
        var vm = new ComponentViewModel(part, part.TemplateName);
        vm.OnSliderChanged = () => RequestResimulation();
        if (vm.LaserConfig != null)
        {
            vm.LaserConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.Simulation.LaserConfig.InputPower))
                    return;
                RequestResimulation();
                RepaintRequested?.Invoke();
            };
        }
        return vm;
    }
}
