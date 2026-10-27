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
    private bool _applyingDefinition;
    private bool _definitionNotificationPending;
    /// <summary>Persists the gate definition on the group (undoable).</summary>
    [RelayCommand]
    private void Save()
    {
        var version = _session.Version;
        _publication.Publish(() => { if (_session.IsCurrent(version)) SaveCore(); });
    }

    private void SaveCore()
    {
        if (!_session.IsConfiguredCurrent || !IsActive) return;
        var version = _session.Version;
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
            savedGroup, BuildRenames(), BuildAssignment(), GateName, CreateDefinitionCallback(this, savedGroup));
        _definitionNotificationPending = false;
        _applyingDefinition = true;
        bool applied;
        try
        {
            applied = _session.TryCommit(version, () =>
            {
                if (ExecuteCommand != null) ExecuteCommand(command);
                else command.Execute();
            });
        }
        finally { _applyingDefinition = false; }
        if (!applied) { _definitionNotificationPending = false; return; }
        if (!IsEditorCurrent(version, savedGroup)) return;

        if (!ReloadPinsAfterSave(version)) return;
        IsSaved = true;
        if (!IsEditorCurrent(version, savedGroup)) return;
        StatusText = string.Format(Translate("MakeGate.Saved"), savedGroup.GroupName);
        if (!IsEditorCurrent(version, savedGroup)) return;
        if (_definitionNotificationPending)
        {
            _definitionNotificationPending = false;
            PublishDefinitionChanged();
        }
        if (!IsEditorCurrent(version, savedGroup)) return;
        GateSaved?.Invoke(this, EventArgs.Empty);
    }

    private bool IsEditorCurrent(long version, ComponentGroup group) =>
        _session.IsCurrent(version) && IsActive && ReferenceEquals(_group, group);

    private static Action CreateDefinitionCallback(MakeGateViewModel editor, ComponentGroup group)
    {
        // Keep this closure separate from Save's commit lambda, which contains the live editor.
        var reference = new WeakReference<MakeGateViewModel>(editor);
        return () => { if (reference.TryGetTarget(out var current)) current.DefinitionApplied(group); };
    }

    private void DefinitionApplied(ComponentGroup savedGroup)
    {
        if (_session.IsStopping) return;
        if (_applyingDefinition) { _definitionNotificationPending = true; return; }
        var version = _session.Version;
        _publication.Publish(() => DefinitionAppliedDisplay(savedGroup, version));
    }

    private void DefinitionAppliedDisplay(ComponentGroup savedGroup, long version)
    {
        if (_session.IsStopping || _session.Version != version) return;
        if (ReferenceEquals(_group, savedGroup))
        {
            foreach (var row in Pins.ToArray())
            {
                if (_session.IsStopping || _session.Version != version) return;
                row.RefreshPinName();
            }
            if (_session.IsStopping || _session.Version != version) return;
            IsSaved = IsCurrentDefinitionSaved(savedGroup);
        }
        if (_session.IsStopping || _session.Version != version) return;
        PublishDefinitionChanged();
    }

    private void PublishDefinitionChanged()
    {
        var version = _session.Version;
        var canvas = _canvas;
        GateDefinitionChanged?.Invoke(this, EventArgs.Empty);
        if (_session.IsCurrent(version) && ReferenceEquals(canvas, _canvas))
            canvas?.RepaintRequested?.Invoke();
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
        var version = _session.Version;
        _publication.Publish(() => { if (_session.IsCurrent(version)) SaveToLibraryCore(); });
    }

    private void SaveToLibraryCore()
    {
        if (!_session.IsConfiguredCurrent || !IsActive) return;
        var version = _session.Version;
        var group = _group;
        if (!IsSaved)
            SaveCore();
        if (!IsSaved || group == null || !IsEditorCurrent(version, group))
            return;
        var stored = false;
        if (!_session.TryCommit(version, () => stored = SaveToLibraryRequested?.Invoke(group, group.GroupName) ?? false)) return;
        if (!IsEditorCurrent(version, group)) return;
        StatusText = stored
            ? string.Format(Translate("MakeGate.SavedToLibrary"), group.GroupName)
            : Translate("MakeGate.LibraryUnavailable");
    }

    /// <summary>Opens the analysis dock on the Logic tab, where the gate can be built into a network.</summary>
    [RelayCommand]
    private void OpenInLogic()
    {
        var version = _session.Version;
        _publication.Publish(() => { if (_session.IsCurrent(version)) OpenLogicRequested?.Invoke(); });
    }

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
    private bool ReloadPinsAfterSave(long version)
    {
        var threshold = Threshold;
        if (!LoadPins(version)) return false;
        Threshold = threshold;
        return _session.IsCurrent(version);
    }
}
