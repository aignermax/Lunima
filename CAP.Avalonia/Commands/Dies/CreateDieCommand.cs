using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>What a new die is made of: name, process, outline size and enabled PDKs.</summary>
/// <param name="Name">Display name of the die.</param>
/// <param name="Process">Process the die is fabricated in; null leaves it unbound.</param>
/// <param name="WidthMicrometers">Outline width.</param>
/// <param name="HeightMicrometers">Outline height.</param>
/// <param name="EnabledPdkNames">PDK subset enabled on the die; null = all PDKs of the process.</param>
public sealed record DieSpec(
    string Name,
    ActiveProcessSelection? Process,
    double WidthMicrometers,
    double HeightMicrometers,
    IReadOnlyList<string>? EnabledPdkNames = null)
{
    /// <summary>The die definition this spec describes (edge zone at its default).</summary>
    public DieDefinition ToDefinition() =>
        new DieDefinition(WidthMicrometers, HeightMicrometers, EnabledPdkNames: EnabledPdkNames).Validated();
}

/// <summary>
/// "New die": adds an empty die to the right of the existing dies. In a single-die design
/// the existing chip becomes the first die of the assembly in the same step — its content
/// and wires move into "Die 1" with the chip's size and process — so both dies end up
/// side by side and one undo restores the single-die design exactly.
/// </summary>
public sealed class CreateDieCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CreateGroupCommand? _convertChip;
    private readonly CreateGroupCommand _newDie;

    /// <summary>
    /// Prepares the command against the canvas's current state (call at the assembly root
    /// or in a single-die design, not inside a group).
    /// </summary>
    /// <param name="canvas">The design canvas.</param>
    /// <param name="spec">The new die.</param>
    /// <param name="existingChip">How the current chip becomes the first die when the design is
    /// still single-die; ignored once the design already is an assembly.</param>
    public CreateDieCommand(DesignCanvasViewModel canvas, DieSpec spec, DieSpec existingChip)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(existingChip);

        var outlines = canvas.Dies.RootDies.Select(DieAssembly.OutlineOf).ToList();
        if (!canvas.Dies.IsAssemblyRoot)
        {
            _convertChip = new CreateGroupCommand(canvas, canvas.Components.ToList(), existingChip.Name)
            {
                Die = existingChip.ToDefinition(),
                ProcessBinding = existingChip.Process,
            };
            outlines.Add(new DieOutline(0, 0, existingChip.WidthMicrometers, existingChip.HeightMicrometers));
        }

        var (x, y) = DieAssembly.NextDieOrigin(outlines);
        _newDie = new CreateGroupCommand(canvas, new List<ComponentViewModel>(), spec.Name)
        {
            Die = spec.ToDefinition(),
            DieOriginX = x,
            DieOriginY = y,
            ProcessBinding = spec.Process,
        };
    }

    /// <inheritdoc/>
    public string Description => "New die";

    /// <summary>The die created by the last execution (null before).</summary>
    public ComponentGroup? CreatedDie => _newDie.CreatedGroup;

    /// <summary>The die the single-die chip became, or null when the design already was an assembly.</summary>
    public ComponentGroup? ConvertedChipDie => _convertChip?.CreatedGroup;

    /// <inheritdoc/>
    public void Execute()
    {
        _convertChip?.Execute();
        _newDie.Execute();
        RefreshScope();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        _newDie.Undo();
        _convertChip?.Undo();
        RefreshScope();
    }

    private void RefreshScope()
    {
        _canvas.Dies.ApplyScopeBounds();
        _canvas.RepaintRequested?.Invoke();
    }
}
