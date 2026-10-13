using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>The editable state of a die: its name, process and definition.</summary>
/// <param name="Name">Die name.</param>
/// <param name="Process">Process binding.</param>
/// <param name="Definition">Outline, edge zone and enabled PDKs.</param>
public sealed record DieState(string Name, ActiveProcessSelection? Process, DieDefinition Definition)
{
    /// <summary>The current state of <paramref name="die"/>.</summary>
    public static DieState Of(ComponentGroup die) =>
        new(die.GroupName, die.ProcessBinding, die.Die ?? throw new ArgumentException("Group is not a die.", nameof(die)));
}

/// <summary>
/// One undoable edit of a die's properties (rename, resize, process, PDKs, edge zone).
/// Resizing keeps the top-left corner; a resize that would overlap another die is refused
/// by <see cref="TryCreate"/>.
/// </summary>
public sealed class UpdateDieCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _die;
    private readonly DieState _before;
    private readonly DieState _after;

    private UpdateDieCommand(DesignCanvasViewModel canvas, ComponentGroup die, DieState before, DieState after)
    {
        _canvas = canvas;
        _die = die;
        _before = before;
        _after = after;
    }

    /// <summary>
    /// Creates the edit, or null with a reason when nothing changes or the new outline
    /// would overlap another die of the assembly.
    /// </summary>
    public static UpdateDieCommand? TryCreate(DesignCanvasViewModel canvas, ComponentGroup die, DieState after, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(die);
        ArgumentNullException.ThrowIfNull(after);
        refusal = null;
        var before = DieState.Of(die);
        if (before == after)
            return null;

        after.Definition.Validated();
        var outline = new DieOutline(
            die.PhysicalX + after.Definition.OutlineOffsetXMicrometers,
            die.PhysicalY + after.Definition.OutlineOffsetYMicrometers,
            after.Definition.WidthMicrometers, after.Definition.HeightMicrometers);
        var others = canvas.Dies.RootDies.Where(d => d != die).Select(DieAssembly.OutlineOf);
        if (DieAssembly.OverlapsAny(outline, others))
        {
            refusal = $"'{after.Name}' would overlap another die at that size — move it apart first.";
            return null;
        }
        return new UpdateDieCommand(canvas, die, before, after);
    }

    /// <inheritdoc/>
    public string Description => $"Edit die {_after.Name}";

    /// <inheritdoc/>
    public void Execute() => Apply(_after);

    /// <inheritdoc/>
    public void Undo() => Apply(_before);

    private void Apply(DieState state)
    {
        _die.GroupName = state.Name;
        _die.ProcessBinding = state.Process;
        _die.Die = state.Definition;
        _canvas.Components.FirstOrDefault(c => c.Component == _die)?.NotifyDimensionsChanged();
        _canvas.Dies.ApplyScopeBounds();
        _canvas.NotifyDiesChanged();
    }
}
