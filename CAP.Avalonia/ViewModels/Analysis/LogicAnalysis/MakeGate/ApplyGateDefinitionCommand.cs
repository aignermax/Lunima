using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// Turns a group into a gate in one undoable step: renames its external pins to the
/// short gate names (A, B, BIAS, Y…), names the group after the gate and persists the
/// pin-role assignment that makes the Logic panel treat it as a gate. Undo restores
/// the previous pin names, group name and assignment.
/// </summary>
public sealed class ApplyGateDefinitionCommand : IUndoableCommand
{
    private readonly ComponentGroup _group;
    private readonly IReadOnlyDictionary<string, string> _renames;
    private readonly TruthTablePinAssignment _assignment;
    private readonly string _gateName;
    private readonly Action? _changed;
    private readonly Dictionary<GroupPin, string> _previousPinNames = new();
    private TruthTablePinAssignment? _previousAssignment;
    private string _previousGroupName = "";

    /// <summary>Creates the command.</summary>
    /// <param name="group">The group that becomes a gate.</param>
    /// <param name="renames">Current external pin name → new name; pins not listed keep their name.</param>
    /// <param name="assignment">The pin-role assignment to persist, expressed in the NEW pin names.</param>
    /// <param name="gateName">New group name; null or whitespace keeps the current one.</param>
    /// <param name="changed">Called after execute and undo (e.g. to repaint the canvas).</param>
    public ApplyGateDefinitionCommand(
        ComponentGroup group,
        IReadOnlyDictionary<string, string> renames,
        TruthTablePinAssignment assignment,
        string? gateName,
        Action? changed = null)
    {
        _group = group;
        _renames = renames;
        _assignment = assignment;
        _gateName = string.IsNullOrWhiteSpace(gateName) ? group.GroupName : gateName.Trim();
        _changed = changed;
    }

    /// <inheritdoc/>
    public string Description => $"Make '{_gateName}' a logic gate";

    /// <inheritdoc/>
    public void Execute()
    {
        _previousAssignment = _group.TruthTablePinAssignment;
        _previousGroupName = _group.GroupName;
        _previousPinNames.Clear();
        foreach (var pin in _group.ExternalPins)
        {
            _previousPinNames[pin] = pin.Name;
            if (_renames.TryGetValue(pin.Name, out var newName))
                pin.Name = newName;
        }
        _group.GroupName = _gateName;
        _group.TruthTablePinAssignment = _assignment.Copy();
        _changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        foreach (var (pin, name) in _previousPinNames)
            pin.Name = name;
        _group.GroupName = _previousGroupName;
        _group.TruthTablePinAssignment = _previousAssignment;
        _changed?.Invoke();
    }
}
