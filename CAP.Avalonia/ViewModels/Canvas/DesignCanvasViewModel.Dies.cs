using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>Die (chiplet) half of <see cref="DesignCanvasViewModel"/>.</summary>
public partial class DesignCanvasViewModel
{
    /// <summary>
    /// Raised after the set of dies, their outlines, processes or PDKs changed, so panels
    /// that list or scope by dies (Chiplets panel, die properties, library) can refresh.
    /// </summary>
    public event Action? DiesChanged;

    /// <summary>
    /// Records the single-die chip size (the Chip Size setting) and applies it to placement
    /// and routing while the single-die root scope is active.
    /// </summary>
    public void SetRootChipSize(double widthMicrometers, double heightMicrometers)
        => Dies.SetRootChipSize(widthMicrometers, heightMicrometers);

    /// <summary>
    /// Routing grid for a freshly loaded group sub-canvas: a die's outline when the group
    /// is (inside) a die, the classic default grid for ordinary groups.
    /// </summary>
    private void InitializeRoutingForEditScope()
    {
        if (Dies?.ActiveDie != null)
            Dies.ApplyScopeBounds();
        else
            Routing.InitializeAStarRouting();
    }

    /// <summary>
    /// Re-bounds placement and routing after entering or leaving a die; ordinary group
    /// editing in a single-die design keeps its previous bounds untouched.
    /// </summary>
    private void ApplyDieScopeAfterEditChange()
    {
        if (Dies.ActiveDie == null && !Dies.IsAssemblyRoot)
            return;
        if (Dies.IsAssemblyRoot)
            RefreshDieInterfaces();
        Dies.ApplyScopeBounds();
        NotifyDiesChanged();
        _ = RecalculateRoutesAsync();
    }

    /// <summary>After a die was dragged on the assembly: re-bound the carrier and notify listeners.</summary>
    private void OnDieDragEnded(ComponentViewModel component)
    {
        if (component.Component is not ComponentGroup { IsDie: true } || !Dies.IsAssemblyRoot)
            return;
        Dies.ApplyScopeBounds();
        NotifyDiesChanged();
    }

    /// <summary>Keeps the die-to-die link markers on their facets while a die moves.</summary>
    private void OnDieMoved(ComponentViewModel component)
    {
        if (component.Component is ComponentGroup { IsDie: true })
            RefreshFacetLinkPaths();
    }

    /// <summary>Notifies <see cref="DiesChanged"/> listeners and repaints the canvas.</summary>
    public void NotifyDiesChanged()
    {
        RefreshFacetLinkPaths();
        DiesChanged?.Invoke();
        RepaintRequested?.Invoke();
    }

    /// <summary>
    /// Brings every root die's external pins in line with its content (see
    /// <see cref="DiePinExposure"/>) and re-registers the pins the assembly can wire to.
    /// </summary>
    public void RefreshDieInterfaces()
    {
        if (IsInGroupEditMode)
            return;

        var wiredOutside = ConnectionManager.Connections
            .SelectMany(c => new[] { c.StartPin, c.EndPin })
            .ToHashSet();
        foreach (var vm in Components.Where(c => c.Component is ComponentGroup { IsDie: true }).ToList())
        {
            var die = (ComponentGroup)vm.Component;
            DiePinExposure.Refresh(die, wiredOutside);
            ResyncGroupPinViewModels(vm, die);
            vm.NotifyDimensionsChanged();
        }
    }

    /// <summary>
    /// Die-to-die facet links are never routed and carry no waveguide: each keeps a
    /// zero-length marker segment on its start facet (no propagation loss — the free-space
    /// coupling model owns the link's transmission), refreshed here whenever dies move, so
    /// the link has persistable geometry and every routing pass leaves it alone.
    /// </summary>
    public void RefreshFacetLinkPaths()
    {
        foreach (var connectionVm in Connections.Where(c => c.Connection.IsCrossChipletFacetLink))
        {
            var connection = connectionVm.Connection;
            var (x1, y1) = connection.StartPin.GetAbsolutePosition();
            if (connection.RoutedPath is { Segments.Count: 1 } existing
                && IsAt(existing.Segments[0].StartPoint, x1, y1) && IsAt(existing.Segments[0].EndPoint, x1, y1))
                continue;
            var path = new CAP_Core.Routing.RoutedPath();
            path.Segments.Add(new CAP_Core.Routing.StraightSegment(x1, y1, x1, y1, connection.StartPin.GetAbsoluteAngle()));
            connection.ReplaceRoutedPath(path);
            connectionVm.NotifyPathChanged();
        }
    }

    private static bool IsAt((double X, double Y) point, double x, double y)
    {
        const double ToleranceMicrometers = 1e-6;
        return Math.Abs(point.X - x) < ToleranceMicrometers && Math.Abs(point.Y - y) < ToleranceMicrometers;
    }

    /// <summary>Replaces the pin view models of a group with one per current external pin.</summary>
    private void ResyncGroupPinViewModels(ComponentViewModel vm, ComponentGroup group)
    {
        foreach (var stale in AllPins.Where(p => p.ParentComponentViewModel == vm).ToList())
            AllPins.Remove(stale);
        foreach (var pin in group.PhysicalPins)
            AllPins.Add(new PinViewModel(pin, vm));
        foreach (var groupPin in group.ExternalPins)
            AllPins.Add(new PinViewModel(groupPin.InternalPin, vm));
    }
}
