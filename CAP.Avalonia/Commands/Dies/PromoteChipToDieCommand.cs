using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// The first edit of "Die 1" in a single-die design (rename, resize, process, PDKs): the chip
/// becomes a real die carrying the edited properties — its content and wires move into it at
/// the chip origin, exactly like the chip does when a second die is added. One undo step
/// restores the single-die design. Until then the chip is only shown as Die 1 (see
/// <see cref="ViewModels.Canvas.DieSurface.ChipDieService"/>), so files and exports of designs
/// whose die was never edited stay unchanged.
/// </summary>
public sealed class PromoteChipToDieCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CreateGroupCommand _promote;

    /// <summary>Prepares the promotion of the canvas's single-die chip with <paramref name="state"/>.</summary>
    public PromoteChipToDieCommand(DesignCanvasViewModel canvas, DieState state)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        ArgumentNullException.ThrowIfNull(state);
        var outline = canvas.ChipDie.Outline;
        _promote = new CreateGroupCommand(canvas, canvas.Components.ToList(), state.Name)
        {
            Die = state.Definition.Validated(),
            DieOriginX = outline.X,
            DieOriginY = outline.Y,
            ProcessBinding = state.Process,
        };
    }

    /// <inheritdoc/>
    public string Description => "Edit die";

    /// <summary>The die the chip became (null before the first execution).</summary>
    public ComponentGroup? CreatedDie => _promote.CreatedGroup;

    /// <inheritdoc/>
    public void Execute()
    {
        _promote.Execute();
        RefreshScope();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        _promote.Undo();
        RefreshScope();
    }

    private void RefreshScope()
    {
        _canvas.Dies.ApplyScopeBounds();
        _canvas.NotifyDiesChanged();
    }
}
