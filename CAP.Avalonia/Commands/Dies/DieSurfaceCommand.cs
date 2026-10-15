using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// Runs an ordinary canvas command (delete, group, ungroup, paste, …) on parts that sit on
/// a die while the assembly root is shown. The parts are children of their die, but those
/// commands work on top-level items: the parts are lifted onto the top level for the
/// duration of the command, and every part the command leaves there — the parts
/// themselves, a new group, ungrouped or pasted parts — is put back onto its die (a known
/// part onto the die it came from, a new one onto <paramref name="defaultDie"/>). Undo and
/// redo do the same around the inner command, so it keeps its own undo logic untouched and
/// the whole action stays one undo step.
/// </summary>
public sealed class DieSurfaceCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Func<IUndoableCommand> _innerFactory;
    private readonly string _description;
    private IUndoableCommand? _inner;
    private readonly IReadOnlyList<ComponentViewModel> _partsToLift;
    private readonly Func<Component, ComponentGroup?> _dieForNewPart;
    private readonly Dictionary<Component, ComponentGroup> _knownDies = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Component> _looseBefore;
    private List<ComponentViewModel> _lowered = new();

    /// <summary>
    /// Wraps the command <paramref name="innerFactory"/> builds — once the parts are lifted, so
    /// it sees them on the top level — acting on <paramref name="partsToLift"/>.
    /// </summary>
    /// <param name="canvas">The design canvas (at the assembly root).</param>
    /// <param name="innerFactory">Builds the command to run on the lifted parts.</param>
    /// <param name="partsToLift">Parts on dies the inner command expects on the top level.</param>
    /// <param name="defaultDie">Die for parts the command creates (a new group, pasted parts).</param>
    /// <param name="description">Undo-history text.</param>
    public DieSurfaceCommand(DesignCanvasViewModel canvas, Func<IUndoableCommand> innerFactory,
        IReadOnlyList<ComponentViewModel> partsToLift, ComponentGroup? defaultDie, string description)
        : this(canvas, innerFactory, partsToLift, _ => defaultDie, description)
    {
    }

    /// <summary>
    /// Wraps the command <paramref name="innerFactory"/> builds; parts it creates go onto the
    /// die <paramref name="dieForNewPart"/> picks for each of them (e.g. the die under a pasted part).
    /// </summary>
    public DieSurfaceCommand(DesignCanvasViewModel canvas, Func<IUndoableCommand> innerFactory,
        IReadOnlyList<ComponentViewModel> partsToLift, Func<Component, ComponentGroup?> dieForNewPart, string description)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
        _description = description ?? "";
        _partsToLift = partsToLift ?? throw new ArgumentNullException(nameof(partsToLift));
        _dieForNewPart = dieForNewPart ?? throw new ArgumentNullException(nameof(dieForNewPart));
        foreach (var part in partsToLift)
        {
            if (part.Component.ParentGroup is ComponentGroup { IsDie: true } die)
                _knownDies[part.Component] = die;
        }
        _looseBefore = new HashSet<Component>(LooseTopLevel().Select(vm => vm.Component), ReferenceEqualityComparer.Instance);
    }

    /// <summary>The wrapped command (null before the first execution).</summary>
    public IUndoableCommand? Inner => _inner;

    /// <inheritdoc/>
    public string Description => _inner?.Description ?? _description;

    /// <inheritdoc/>
    public void Execute()
    {
        Lift(_partsToLift.Where(p => p.Component.ParentGroup is ComponentGroup { IsDie: true }));
        _inner ??= _innerFactory();
        _inner.Execute();
        _lowered = LowerNewTopLevel();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (_inner == null)
            return;
        Lift(_lowered.Where(p => p.Component.ParentGroup is ComponentGroup { IsDie: true }));
        _inner.Undo();
        LowerNewTopLevel();
    }

    private IEnumerable<ComponentViewModel> LooseTopLevel() =>
        _canvas.Components.Where(c => c.Component is not ComponentGroup { IsDie: true }).ToList();

    private void Lift(IEnumerable<ComponentViewModel> parts)
    {
        try
        {
            _canvas.BeginCommandExecution();
            foreach (var part in parts.ToList())
            {
                var die = (ComponentGroup)part.Component.ParentGroup!;
                _knownDies[part.Component] = die;
                die.RemoveChild(part.Component);
                part.X = part.Component.PhysicalX;
                part.Y = part.Component.PhysicalY;
                _canvas.AttachTopLevel(part);
            }
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
    }

    private List<ComponentViewModel> LowerNewTopLevel()
    {
        var lowered = new List<ComponentViewModel>();
        try
        {
            _canvas.BeginCommandExecution();
            foreach (var vm in LooseTopLevel().Where(vm => !_looseBefore.Contains(vm.Component)))
            {
                var die = _knownDies.GetValueOrDefault(vm.Component) ?? _dieForNewPart(vm.Component);
                if (die == null)
                    continue;
                _canvas.DetachTopLevel(vm);
                die.AddChild(vm.Component);
                _knownDies[vm.Component] = die;
                lowered.Add(vm);
            }
        }
        finally
        {
            _canvas.EndCommandExecution();
        }
        _canvas.RefreshDieInterfaces();
        _canvas.Surface.Prune();
        _canvas.InvalidateSimulation();
        _canvas.NotifyDiesChanged();
        return lowered;
    }
}
