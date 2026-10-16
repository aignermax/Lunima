using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// Save half of <see cref="MakeGateViewModel"/>: renames the pins, persists the
/// pin-role assignment (the same record a plain truth-table extraction persists, which
/// is what makes a group a gate), saves to the library on request and hands over to
/// the Logic tab.
/// </summary>
public partial class MakeGateViewModel
{
    /// <summary>Persists the gate definition on the group (undoable).</summary>
    [RelayCommand]
    private void Save()
    {
        if (_group == null || !HasResult)
        {
            StatusText = Translate("MakeGate.SimulateFirst");
            return;
        }
        var problem = ValidatePins() ?? ValidateGateName();
        if (problem != null)
        {
            StatusText = problem;
            return;
        }

        var savedGroup = _group;
        var command = new ApplyGateDefinitionCommand(
            savedGroup, BuildRenames(), BuildAssignment(), GateName, () =>
            {
                // The command can be undone after the editor has moved to another group.
                if (ReferenceEquals(_group, savedGroup))
                {
                    foreach (var row in Pins) row.RefreshPinName();
                    IsSaved = IsCurrentDefinitionSaved(savedGroup);
                }
                GateDefinitionChanged?.Invoke(this, EventArgs.Empty);
                _canvas?.RepaintRequested?.Invoke();
            });
        if (ExecuteCommand != null)
            ExecuteCommand(command);
        else
            command.Execute();

        ReloadPinsAfterSave();
        IsSaved = true;
        StatusText = string.Format(Translate("MakeGate.Saved"), _group.GroupName);
        GateSaved?.Invoke(this, EventArgs.Empty);
    }

    private bool IsCurrentDefinitionSaved(ComponentGroup group) =>
        group.TruthTablePinAssignment is { } saved && group.GroupName == GateName.Trim() &&
        saved.InputPinNames.SequenceEqual(InputRows.Select(FinalName)) &&
        saved.OutputPinNames.SequenceEqual(OutputRows.Select(FinalName)) &&
        saved.BiasPinNames.SequenceEqual(BiasRows.Select(FinalName)) &&
        saved.Threshold == Math.Clamp(Threshold, GateThresholdAdvisor.MinThreshold, GateThresholdAdvisor.MaxThreshold);

    /// <summary>Saves the gate (if not saved yet) and adds it to the library's Saved Groups.</summary>
    [RelayCommand]
    private void SaveToLibrary()
    {
        if (!IsSaved)
            Save();
        if (!IsSaved || _group == null)
            return;
        var stored = SaveToLibraryRequested?.Invoke(_group, _group.GroupName) ?? false;
        StatusText = stored
            ? string.Format(Translate("MakeGate.SavedToLibrary"), _group.GroupName)
            : Translate("MakeGate.LibraryUnavailable");
    }

    /// <summary>Opens the analysis dock on the Logic tab, where the gate can be built into a network.</summary>
    [RelayCommand]
    private void OpenInLogic() => OpenLogicRequested?.Invoke();

    /// <summary>A gate name must be non-empty and not used by another group on the canvas.</summary>
    private string? ValidateGateName()
    {
        if (string.IsNullOrWhiteSpace(GateName))
            return Translate("MakeGate.GateNameMissing");
        var name = GateName.Trim();
        var taken = _canvas?.Components
            .Select(c => c.Component)
            .OfType<ComponentGroup>()
            .Any(g => !ReferenceEquals(g, _group) && g.GroupName == name) ?? false;
        return taken ? string.Format(Translate("MakeGate.GateNameTaken"), name) : null;
    }

    /// <summary>Current pin name → short name for every pin whose name changes.</summary>
    private Dictionary<string, string> BuildRenames() => Pins
        .Where(p => FinalName(p) != p.PinName)
        .ToDictionary(p => p.PinName, FinalName);

    /// <summary>The assignment in the new pin names; keeps a previous register flag.</summary>
    private TruthTablePinAssignment BuildAssignment() => new()
    {
        InputPinNames = InputRows.Select(FinalName).ToList(),
        OutputPinNames = OutputRows.Select(FinalName).ToList(),
        BiasPinNames = BiasRows.Select(FinalName).ToList(),
        Threshold = Math.Clamp(Threshold, GateThresholdAdvisor.MinThreshold, GateThresholdAdvisor.MaxThreshold),
        IsRegister = _group?.TruthTablePinAssignment?.IsRegister ?? false,
    };

    /// <summary>The pin rows address pins by name, so they are rebuilt from the renamed group.</summary>
    private void ReloadPinsAfterSave()
    {
        var threshold = Threshold;
        LoadPins();
        Threshold = threshold;
    }
}
