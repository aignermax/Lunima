using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Canvas.DieSurface;

/// <summary>
/// A die is a work surface, not a group you enter: at the root of a die assembly the parts
/// on every die are placed, selected, moved and wired right there. The model keeps them as
/// children of their die (a die is the top-level group that owns its process and outline);
/// this service gives each of them a stable <see cref="ComponentViewModel"/> the canvas
/// interacts with, finds the part under the pointer, and checks where a part may go — on its
/// die, clear of the die's other parts. In the System workspace the content is locked: the
/// unit of work there is the die, its parts are only shown.
/// </summary>
public sealed class DieSurfaceService
{
    private readonly ObservableCollection<ComponentViewModel> _components;
    private readonly GroupEditService _groups;
    private readonly Dictionary<Component, ComponentViewModel> _viewModels = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates the service over the canvas's top-level components.</summary>
    public DieSurfaceService(ObservableCollection<ComponentViewModel> components, GroupEditService groups)
    {
        _components = components;
        _groups = groups;
    }

    /// <summary>
    /// True in the System workspace: die content is dimmed and locked — clicks select the die,
    /// parts and their wires cannot be picked, moved or wired.
    /// </summary>
    public bool IsContentLocked { get; set; }

    /// <summary>True at the root of a die assembly (the dies shown side by side).</summary>
    public bool IsActive => !_groups.IsInGroupEditMode && _components.Any(c => c.Component is ComponentGroup { IsDie: true });

    /// <summary>True when the parts on the dies can be worked on directly right now.</summary>
    public bool IsEditable => IsActive && !IsContentLocked;

    /// <summary>The die groups at the root, in canvas order.</summary>
    public IReadOnlyList<ComponentGroup> Dies =>
        _components.Select(c => c.Component).OfType<ComponentGroup>().Where(g => g.IsDie).ToList();

    /// <summary>The view models of every part sitting directly on a die (nested groups count as one part).</summary>
    public IReadOnlyList<ComponentViewModel> Items
    {
        get
        {
            if (!IsActive)
                return Array.Empty<ComponentViewModel>();
            Prune();
            return Dies.SelectMany(d => d.ChildComponents).Select(ViewModelFor).ToList();
        }
    }

    /// <summary>True when <paramref name="viewModel"/> stands for a part sitting directly on a die.</summary>
    public bool IsItem(ComponentViewModel? viewModel) =>
        viewModel?.Component.ParentGroup is ComponentGroup { IsDie: true } && IsActive;

    /// <summary>The die <paramref name="viewModel"/>'s part sits on, or null for anything else.</summary>
    public ComponentGroup? DieOf(ComponentViewModel? viewModel) =>
        IsItem(viewModel) ? (ComponentGroup)viewModel!.Component.ParentGroup! : null;

    /// <summary>
    /// The stable view model of a part on a die, created on first use. Its cached position is
    /// brought in line with the part, which moves with its die.
    /// </summary>
    public ComponentViewModel ViewModelFor(Component part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (!_viewModels.TryGetValue(part, out var viewModel))
        {
            viewModel = ViewModelCreated?.Invoke(part) ?? new ComponentViewModel(part, part.TemplateName);
            _viewModels[part] = viewModel;
        }
        if (viewModel.X != part.PhysicalX) viewModel.X = part.PhysicalX;
        if (viewModel.Y != part.PhysicalY) viewModel.Y = part.PhysicalY;
        return viewModel;
    }

    /// <summary>
    /// Builds the view model of a part the first time it is touched (wired by the canvas so a
    /// light source gets its laser hooks like a top-level part); null uses a plain view model.
    /// </summary>
    public Func<Component, ComponentViewModel>? ViewModelCreated { get; set; }

    /// <summary>The topmost part under (<paramref name="x"/>, <paramref name="y"/>) on any die, or null.</summary>
    public ComponentViewModel? HitTest(double x, double y)
    {
        if (!IsEditable)
            return null;
        foreach (var die in Dies.Reverse())
        {
            if (!DieAssembly.OutlineOf(die).Contains(x, y))
                continue;
            for (int i = die.ChildComponents.Count - 1; i >= 0; i--)
            {
                var part = die.ChildComponents[i];
                var (px, py, pw, ph) = DiePlacement.FootprintOf(part);
                if (x >= px && x <= px + pw && y >= py && y <= py + ph)
                    return ViewModelFor(part);
            }
            return null;
        }
        return null;
    }

    /// <summary>
    /// True when a footprint at (<paramref name="x"/>, <paramref name="y"/>) fits on a die: the
    /// die of <paramref name="moving"/> when it already sits on one (parts stay on their die),
    /// else the die under the footprint's center — inside the outline and clear of the die's
    /// other parts (and of <paramref name="alsoMoving"/>, which move along).
    /// </summary>
    public bool CanPlace(double x, double y, double width, double height, Component? moving = null,
        IReadOnlySet<Component>? alsoMoving = null)
    {
        var die = moving?.ParentGroup as ComponentGroup is { IsDie: true } own
            ? own
            : DieAssembly.DieAt(_components.Select(c => c.Component), x + width / 2, y + height / 2);
        if (die == null)
            return false;
        var exclude = new HashSet<Component>(alsoMoving ?? new HashSet<Component>(), ReferenceEqualityComparer.Instance);
        if (moving != null)
            exclude.Add(moving);
        return DiePlacement.IsFree(die, x, y, width, height, exclude);
    }

    /// <summary>
    /// Whether every part of <paramref name="selection"/> may move by the delta (each stays on
    /// its die, clear of the parts that do not move), or null when the selection is not made
    /// of parts on dies only.
    /// </summary>
    public bool? CanMoveTogether(IReadOnlyCollection<ComponentViewModel> selection, double deltaX, double deltaY)
    {
        if (selection.Count == 0 || !selection.All(IsItem))
            return null;
        var moving = new HashSet<Component>(selection.Select(s => s.Component), ReferenceEqualityComparer.Instance);
        foreach (var item in selection)
        {
            var (x, y, w, h) = DiePlacement.FootprintOf(item.Component);
            if (!CanPlace(x + deltaX, y + deltaY, w, h, item.Component, moving))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Uses <paramref name="viewModel"/> from now on for its part (a part that was briefly on
    /// the top level keeps the view model that is selected).
    /// </summary>
    public void Adopt(ComponentViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModels[viewModel.Component] = viewModel;
    }

    /// <summary>Forgets view models of parts that left their die (deleted, grouped, moved into a group).</summary>
    public void Prune()
    {
        foreach (var stale in _viewModels.Keys.Where(p => p.ParentGroup is not ComponentGroup { IsDie: true }).ToList())
            _viewModels.Remove(stale);
    }
}
