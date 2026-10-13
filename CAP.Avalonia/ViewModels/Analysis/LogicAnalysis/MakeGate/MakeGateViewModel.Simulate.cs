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
    private async Task Simulate()
    {
        if (IsProcessing || _group == null)
            return;
        var problem = ValidatePins();
        if (problem != null)
        {
            StatusText = problem;
            return;
        }

        ResetResult();
        _simulateCts = new CancellationTokenSource();
        IsProcessing = true;
        StatusText = Translate("MakeGate.Simulating");
        try
        {
            var table = await _extractor.ExtractAsync(
                _group,
                InputRows.Select(p => p.PinName).ToList(),
                OutputRows.Select(p => p.PinName).ToList(),
                BiasRows.Select(p => p.PinName).ToList(),
                ExtractionThreshold,
                ResolveWavelengthNm(),
                _simulateCts.Token);
            ShowResult(table);
        }
        catch (OperationCanceledException)
        {
            StatusText = Translate("MakeGate.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Translate("MakeGate.Failed"), ex.Message);
        }
        finally
        {
            IsProcessing = false;
            _simulateCts?.Dispose();
            _simulateCts = null;
        }
    }

    /// <summary>Cancels a running simulation.</summary>
    [RelayCommand]
    private void CancelSimulation() => _simulateCts?.Cancel();

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
    private void ShowResult(TruthTable table)
    {
        _table = table;
        Rows.Clear();
        var shortNames = Pins.ToDictionary(p => p.PinName, FinalName);
        var fullScale = table.Rows.SelectMany(r => r.Outputs.Values).Select(v => v.Power).DefaultIfEmpty(0).Max();
        foreach (var row in table.Rows)
        {
            var bits = string.Join(" ", table.InputPinNames.Select(name => row.InputBits[name] ? "1" : "0"));
            foreach (var output in table.OutputPinNames)
                Rows.Add(new GateResultRowViewModel(bits, shortNames[output], row.Outputs[output].Power, fullScale));
        }
        HasResult = true;
        StatusText = string.Format(Translate("MakeGate.SimulatedRows"), table.Rows.Count);
        ApplyFunctionExpectations();
        ApplySuggestedThreshold();
    }

    /// <summary>The active laser's wavelength, falling back to the standard red wavelength.</summary>
    private int ResolveWavelengthNm() =>
        _canvas?.Components.FirstOrDefault(c => c.IsLightSource)?.LaserConfig?.WavelengthNm
        ?? StandardWaveLengths.RedNM;
}
