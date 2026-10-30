using System.Globalization;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>
/// Automatic-threshold half of <see cref="TruthTableViewModel"/>: after every
/// extraction the panel suggests the threshold across the largest gap between the
/// measured output powers (the same <see cref="GateThresholdAdvisor"/> the Make-gate
/// steps use, without a declared function), shows the margin in dB, warns when no
/// gap separates the levels, and re-extracts at the suggestion on request. It also
/// hands over to the Logic tab once the group is a gate.
/// </summary>
public partial class TruthTableViewModel
{
    private readonly GateThresholdAdvisor _thresholdAdvisor = new();

    /// <summary>Threshold suggested from the last extraction (null when none).</summary>
    [ObservableProperty]
    private double? _suggestedThreshold;

    /// <summary>"Suggested 0.122 · margin 35.7 dB" (or the overlap warning) under the result.</summary>
    [ObservableProperty]
    private string _thresholdSuggestionText = "";

    /// <summary>True when the suggestion differs from the current threshold (offers "Use").</summary>
    [ObservableProperty]
    private bool _canUseSuggestedThreshold;

    /// <summary>Opens the analysis dock on the Logic tab (wired by the main window).</summary>
    public Action? OpenLogicRequested { get; set; }

    /// <summary>Moves the threshold to the suggestion and re-extracts at it.</summary>
    [RelayCommand]
    private async Task UseSuggestedThreshold()
    {
        if (_work.IsStopping || SuggestedThreshold is not { } suggested)
            return;
        Threshold = suggested;
        await ExtractCommand.ExecuteAsync(null);
    }

    /// <summary>Opens the Logic tab, where the extracted gate can be built into a network.</summary>
    [RelayCommand]
    private void OpenInLogic() { if (!_work.IsStopping) OpenLogicRequested?.Invoke(); }

    /// <summary>Computes the largest-gap suggestion for a freshly extracted table.</summary>
    private void SuggestThreshold(TruthTable table, AnalysisSweepOperation operation)
    {
        var samples = table.Rows
            .SelectMany(row => row.Outputs.Values)
            .Select(value => new GatePowerSample(value.Power, null))
            .ToList();
        var suggestion = _thresholdAdvisor.Suggest(samples);
        PublishStep(operation, () => SuggestedThreshold = suggestion.LevelsOverlap ? null : suggestion.Threshold);
        PublishStep(operation, () => ThresholdSuggestionText = suggestion.LevelsOverlap
            ? Translate("TruthTable.NoThresholdGap")
            : string.Format(CultureInfo.InvariantCulture, Translate("MakeGate.Suggestion"),
                suggestion.Threshold, suggestion.MarginDb));
        PublishStep(operation, RefreshCanUseSuggestedThreshold);
    }

    private void ClearThresholdSuggestion(Func<bool> isCurrent)
    {
        SuggestedThreshold = null;
        if (!isCurrent()) return;
        ThresholdSuggestionText = "";
        if (!isCurrent()) return;
        CanUseSuggestedThreshold = false;
    }

    partial void OnThresholdChanged(double value)
    {
        if (!_revertingPinCheck) _work.Invalidate();
        RefreshCanUseSuggestedThreshold();
    }

    private void RefreshCanUseSuggestedThreshold() =>
        CanUseSuggestedThreshold = SuggestedThreshold is { } suggested && Math.Abs(suggested - Threshold) > ThresholdTolerance;

    /// <summary>Thresholds closer than this count as equal (the field shows three decimals).</summary>
    private const double ThresholdTolerance = 5e-4;
}
