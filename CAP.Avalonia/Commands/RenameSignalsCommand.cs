using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Renames logic signals inside a cell: every gate (the cell itself or any nested gate group)
/// whose input or output pins carry a signal in <c>renames</c> gets the new name — so two
/// instances of one library cell (a full adder) become distinct parts of a larger network
/// (A→A1, S→S1, …). Undo restores every gate's previous names.
/// </summary>
public sealed class RenameSignalsCommand : IUndoableCommand
{
    private readonly List<(TruthTablePinAssignment Roles, Dictionary<string, string>? Inputs, Dictionary<string, string>? Outputs)> _before = new();
    private readonly IReadOnlyDictionary<string, string> _renames;
    private readonly ComponentGroup _cell;

    /// <summary>Creates the command for <paramref name="cell"/>.</summary>
    public RenameSignalsCommand(ComponentGroup cell, IReadOnlyDictionary<string, string> renames)
    {
        _cell = cell;
        _renames = renames;
        foreach (var roles in Gates(cell))
            _before.Add((roles, Copy(roles.InputSignalNames), Copy(roles.OutputSignalNames)));
    }

    /// <summary>How many gate pins carry a renamed signal (known after <see cref="Execute"/>).</summary>
    public int RenamedPins { get; private set; }

    /// <inheritdoc/>
    public string Description => $"Rename signals in '{_cell.GroupName}'";

    /// <inheritdoc/>
    public void Execute()
    {
        RenamedPins = 0;
        foreach (var (roles, _, _) in _before)
        {
            RenamedPins += Rename(roles.InputSignalNames);
            RenamedPins += Rename(roles.OutputSignalNames);
        }
    }

    /// <inheritdoc/>
    public void Undo()
    {
        foreach (var (roles, inputs, outputs) in _before)
        {
            roles.InputSignalNames = Copy(inputs);
            roles.OutputSignalNames = Copy(outputs);
        }
    }

    private int Rename(Dictionary<string, string>? names)
    {
        if (names == null) return 0;
        var changed = 0;
        foreach (var pin in names.Keys.ToList())
        {
            if (!_renames.TryGetValue(names[pin], out var renamed)) continue;
            names[pin] = renamed;
            changed++;
        }
        return changed;
    }

    private static IEnumerable<TruthTablePinAssignment> Gates(ComponentGroup cell) =>
        new[] { cell }.Concat(cell.GetAllComponentsRecursive().OfType<ComponentGroup>())
            .Select(g => g.TruthTablePinAssignment)
            .Where(roles => roles != null)
            .Select(roles => roles!);

    private static Dictionary<string, string>? Copy(Dictionary<string, string>? names) =>
        names == null ? null : new Dictionary<string, string>(names);
}
