using System.Collections.ObjectModel;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>
/// Decides which rectangle bounds placement and routing on the canvas. A single-die design
/// keeps the classic chip rectangle (the root chip size). Inside a die (group edit mode on
/// a die) everything is bounded by that die's outline. At the root of a multi-die assembly
/// the bound is the carrier around all dies, with room to move dies apart.
/// </summary>
public sealed class DieScopeService
{
    /// <summary>Free space kept around the dies of an assembly so they can be dragged apart.</summary>
    public const double CarrierMarginMicrometers = 2_000.0;

    private const double DefaultRootChipMicrometers = 5_000.0;

    private readonly ObservableCollection<ComponentViewModel> _components;
    private readonly GroupEditService _groups;
    private readonly ComponentPlacementService _placement;
    private readonly RoutingOrchestrator _routing;

    /// <summary>Creates the service over the canvas collections and services it scopes.</summary>
    public DieScopeService(
        ObservableCollection<ComponentViewModel> components,
        GroupEditService groups,
        ComponentPlacementService placement,
        RoutingOrchestrator routing)
    {
        _components = components;
        _groups = groups;
        _placement = placement;
        _routing = routing;
    }

    private double _rootChipWidth = DefaultRootChipMicrometers;
    private double _rootChipHeight = DefaultRootChipMicrometers;

    /// <summary>
    /// True while the placement bounds hold the single-die chip rectangle. Callers that set
    /// the chip bounds directly (scripted designs, older code paths) then define the root
    /// chip size, exactly as before dies existed.
    /// </summary>
    private bool _placementHoldsRootChip = true;

    /// <summary>Width of the single-die chip (the design's chip size setting).</summary>
    public double RootChipWidthMicrometers => _placementHoldsRootChip ? _placement.ChipMaxX : _rootChipWidth;

    /// <summary>Height of the single-die chip (the design's chip size setting).</summary>
    public double RootChipHeightMicrometers => _placementHoldsRootChip ? _placement.ChipMaxY : _rootChipHeight;

    /// <summary>
    /// The die the user works inside: the die around the group being edited (a group on a
    /// die is entered straight from the assembly), or null at the root and inside ordinary
    /// groups of a single-die design.
    /// </summary>
    public ComponentGroup? ActiveDie =>
        _groups.IsInGroupEditMode
            ? _groups.BreadcrumbPath.FirstOrDefault(g => g.IsDie) ?? DieAssembly.DieOf(_groups.CurrentEditGroup)
            : null;

    /// <summary>True at the root of a multi-die assembly (dies shown side by side).</summary>
    public bool IsAssemblyRoot =>
        !_groups.IsInGroupEditMode && DieAssembly.IsAssembly(_components.Select(c => c.Component));

    /// <summary>The die groups at the root, empty inside a die or in a single-die design.</summary>
    public IReadOnlyList<ComponentGroup> RootDies =>
        _groups.IsInGroupEditMode
            ? Array.Empty<ComponentGroup>()
            : DieAssembly.DiesOf(_components.Select(c => c.Component));

    /// <summary>
    /// Records the single-die chip size and, when the root single-die scope is active,
    /// applies it to placement and routing.
    /// </summary>
    public void SetRootChipSize(double widthMicrometers, double heightMicrometers)
    {
        _rootChipWidth = widthMicrometers;
        _rootChipHeight = heightMicrometers;
        _placementHoldsRootChip = false;
        ApplyScopeBounds();
    }

    /// <summary>The rectangle that bounds placement and routing in the current scope.</summary>
    public DieOutline CurrentBounds() => ScopeBounds().Bounds;

    private (DieOutline Bounds, bool IsRootChip) ScopeBounds()
    {
        if (ActiveDie is { } die)
            return (DieAssembly.OutlineOf(die), false);
        if (_groups.IsInGroupEditMode)
            return (RootChipOutline(), true);
        var carrier = DieAssembly.CarrierOutline(RootDies);
        return carrier is { } outline
            ? (new DieOutline(
                outline.X - CarrierMarginMicrometers,
                outline.Y - CarrierMarginMicrometers,
                outline.Width + 2 * CarrierMarginMicrometers,
                outline.Height + 2 * CarrierMarginMicrometers), false)
            : (RootChipOutline(), true);
    }

    /// <summary>Applies <see cref="CurrentBounds"/> to placement and, optionally, the A* grid.</summary>
    public void ApplyScopeBounds(bool reinitializeRouting = true)
    {
        var bounds = ApplyPlacementBounds();
        // At the assembly root a waveguide may only run on a die, never across the carrier.
        _routing.SetAllowedRoutingRegions(RoutingRegions());
        if (reinitializeRouting)
            _routing.InitializeAStarRouting(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
    }

    /// <summary>
    /// The routing grid rebuild still running after <see cref="ApplyScopeBoundsInBackground"/>
    /// (completed when none is pending).
    /// </summary>
    public Task PendingGridRebuild { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Like <see cref="ApplyScopeBounds"/>, but only the placement bounds change right away; the
    /// routing regions and A* grid are rebuilt off the UI thread. Used after a die move, where a
    /// synchronous rebuild of the carrier grid stalled the drop for a second or more.
    /// </summary>
    public Task ApplyScopeBoundsInBackground()
    {
        var bounds = ApplyPlacementBounds();
        PendingGridRebuild = _routing.ReinitializeAStarRoutingInBackgroundAsync(
            bounds.X, bounds.Y, bounds.Right, bounds.Bottom, RoutingRegions());
        return PendingGridRebuild;
    }

    private DieOutline ApplyPlacementBounds()
    {
        if (_placementHoldsRootChip)
        {
            _rootChipWidth = _placement.ChipMaxX;
            _rootChipHeight = _placement.ChipMaxY;
        }
        var (bounds, isRootChip) = ScopeBounds();
        _placementHoldsRootChip = isRootChip;
        _placement.ChipMinX = bounds.X;
        _placement.ChipMinY = bounds.Y;
        _placement.ChipMaxX = bounds.Right;
        _placement.ChipMaxY = bounds.Bottom;
        return bounds;
    }

    private List<(double X, double Y, double Width, double Height)>? RoutingRegions() =>
        IsAssemblyRoot
            ? RootDies.Select(DieAssembly.OutlineOf).Select(o => (o.X, o.Y, o.Width, o.Height)).ToList()
            : null;

    private DieOutline RootChipOutline() => new(0, 0, _rootChipWidth, _rootChipHeight);
}
