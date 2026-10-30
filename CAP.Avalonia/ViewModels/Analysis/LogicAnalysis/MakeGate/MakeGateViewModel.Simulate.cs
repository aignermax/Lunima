using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.ComponentHelpers;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// Simulation half of <see cref="MakeGateViewModel"/>: validates the pin roles, runs
/// the existing <see cref="TruthTableExtractor"/> off the UI thread with cancellation
/// and turns the result into power-bar rows.
/// </summary>
public partial class MakeGateViewModel
{
    private Func<bool>? _resultPublicationGuard;
    private bool _simulationAdmitted;
    private bool CanPublishResult => _resultPublicationGuard?.Invoke() != false;
    /// <summary>
    /// Threshold handed to the extractor. The rows are re-classified locally against
    /// the suggested or user threshold afterwards, so its value only has to be valid.
    /// </summary>
    private const double ExtractionThreshold = 0.5;

    /// <summary>Input pins in the order they become truth-table columns (top to bottom in the list).</summary>
    private IReadOnlyList<GatePinRowViewModel> InputRows => Pins.Where(p => p.Role == GatePinRole.Input).ToList();

    private IReadOnlyList<GatePinRowViewModel> OutputRows => Pins.Where(p => p.Role == GatePinRole.Output).ToList();

    private IReadOnlyList<GatePinRowViewModel> BiasRows => Pins.Where(p => p.Role == GatePinRole.Bias).ToList();

    /// <summary>Runs the truth-table extraction for the chosen pin roles.</summary>
    [RelayCommand]
    private Task Simulate() => _session.RunAsync(SimulateCoreAsync);

    private async Task SimulateCoreAsync(CancellationToken stop)
    {
        if (_simulationAdmitted || IsProcessing || _group == null || !IsActive || !_session.IsConfiguredCurrent)
            return;
        var problem = ValidatePins();
        if (problem != null)
        {
            StatusText = problem;
            return;
        }

        AnalysisSweepOperation? operation = null;
        _simulationAdmitted = true;
        try
        {
            operation = _session.Begin(stop);
            if (operation == null) return;
            var version = _session.Version;
            await _publication.PublishAsync(() =>
            {
                operation.ThrowIfObsolete();
                ResetResultDisplay(version);
                operation.ThrowIfObsolete();
                IsProcessing = true;
                operation.ThrowIfObsolete();
                StatusText = Translate("MakeGate.Simulating");
                operation.ThrowIfObsolete();
            });
            operation.ThrowIfObsolete();
            var shortNames = Pins.ToDictionary(pin => pin.PinName, FinalName);
            var table = await _extractor.ExtractAsync(
                _group,
                InputRows.Select(p => p.PinName).ToList(),
                OutputRows.Select(p => p.PinName).ToList(),
                BiasRows.Select(p => p.PinName).ToList(),
                ExtractionThreshold,
                ResolveWavelengthNm(),
                operation.Token);
            await _publication.PublishAsync(() =>
                operation.TryPublish(() => ShowResult(table, shortNames, () => operation.CanPublish)));
        }
        catch (OperationCanceledException)
        {
            if (operation?.IsContextCurrent == true) StatusText = Translate("MakeGate.Cancelled");
        }
        catch (Exception ex)
        {
            if (operation?.CanPublish == true)
                StatusText = string.Format(Translate("MakeGate.Failed"), ex.Message);
        }
        finally
        {
            _simulationAdmitted = false;
            try { if (operation != null) IsProcessing = false; }
            finally { await _session.CompleteAsync(operation); }
        }
    }

    /// <summary>Cancels a running simulation.</summary>
    [RelayCommand]
    private void CancelSimulation()
    {
        if (!IsProcessing || !_session.IsConfiguredCurrent) return;
        _session.Cancel();
        StatusText = Translate("MakeGate.Cancelled");
    }

    /// <summary>
    /// Checks the roles and names: 1 to <see cref="TruthTableExtractor.MaxLogicInputs"/>
    /// inputs, at least one output, and non-empty, unique names for every used pin.
    /// </summary>
    /// <returns>A readable problem, or null when the definition is complete.</returns>
    private string? ValidatePins()
    {
        var inputs = InputRows.Count;
        if (inputs == 0 || OutputRows.Count == 0)
            return Translate("MakeGate.NeedInputAndOutput");
        if (inputs > TruthTableExtractor.MaxLogicInputs)
            return string.Format(Translate("MakeGate.TooManyInputs"), TruthTableExtractor.MaxLogicInputs);

        var used = Pins.Where(p => p.Role != GatePinRole.Unused).ToList();
        if (used.Any(p => string.IsNullOrWhiteSpace(p.Name)))
            return Translate("MakeGate.NameMissing");
        var duplicate = Pins.GroupBy(p => FinalName(p)).FirstOrDefault(g => g.Count() > 1)?.Key;
        return duplicate == null ? null : string.Format(Translate("MakeGate.NameDuplicate"), duplicate);
    }

    /// <summary>The name a pin carries after saving: its short name when used, else its current name.</summary>
    private static string FinalName(GatePinRowViewModel pin) =>
        pin.Role == GatePinRole.Unused || string.IsNullOrWhiteSpace(pin.Name) ? pin.PinName : pin.Name.Trim();

    /// <summary>Builds one power-bar row per input pattern and output, then suggests the threshold.</summary>
    private void ShowResult(TruthTable table, IReadOnlyDictionary<string, string> shortNames, Func<bool> canPublish)
    {
        var previous = _resultPublicationGuard;
        _resultPublicationGuard = canPublish;
        try
        {
            if (!CanPublishResult) return;
            _table = table;
            Rows.Clear();
            if (!CanPublishResult) return;
            var fullScale = table.Rows.SelectMany(r => r.Outputs.Values).Select(v => v.Power).DefaultIfEmpty(0).Max();
            foreach (var row in table.Rows)
            {
                var bits = string.Join(" ", table.InputPinNames.Select(name => row.InputBits[name] ? "1" : "0"));
                foreach (var output in table.OutputPinNames)
                {
                    if (!CanPublishResult) return;
                    Rows.Add(new GateResultRowViewModel(bits, shortNames[output], row.Outputs[output].Power, fullScale));
                }
            }
            if (!CanPublishResult) return;
            HasResult = true;
            if (!CanPublishResult) return;
            StatusText = string.Format(Translate("MakeGate.SimulatedRows"), table.Rows.Count);
            if (!CanPublishResult) return;
            ApplyFunctionExpectations();
            if (!CanPublishResult) return;
            ApplySuggestedThreshold();
        }
        finally { _resultPublicationGuard = previous; }
    }

    /// <summary>The active laser's wavelength, falling back to the standard red wavelength.</summary>
    private int ResolveWavelengthNm() =>
        _canvas?.Components.FirstOrDefault(c => c.IsLightSource)?.LaserConfig?.WavelengthNm
        ?? StandardWaveLengths.RedNM;
}
