using System.Globalization;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// Threshold half of <see cref="MakeGateViewModel"/>: the intended function marks the
/// rows expected to read 0 and 1, <see cref="GateThresholdAdvisor"/> suggests the
/// threshold between them (or across the largest power gap for a custom gate) and the
/// rows re-classify live whenever the threshold moves.
/// </summary>
public partial class MakeGateViewModel
{
    /// <summary>Default threshold before anything was simulated.</summary>
    private const double DefaultThreshold = 0.5;

    /// <summary>Normalized power threshold; an output at or above it reads 1.</summary>
    [ObservableProperty]
    private double _threshold = DefaultThreshold;

    /// <summary>The automatically suggested threshold (null before a simulation).</summary>
    [ObservableProperty]
    private double? _suggestedThreshold;

    /// <summary>"Suggested 0.122 · margin 35.7 dB" line under the threshold.</summary>
    [ObservableProperty]
    private string _suggestionText = "";

    /// <summary>True when the expected-0 and expected-1 levels overlap — no threshold separates them.</summary>
    [ObservableProperty]
    private bool _hasOverlap;

    /// <summary>Summary of how the rows read against the intended function.</summary>
    [ObservableProperty]
    private string _matchText = "";

    /// <summary>True when every row reads what the intended function expects.</summary>
    [ObservableProperty]
    private bool _matchesFunction;

    /// <summary>True after a manual override moved the threshold off the suggestion (offers "Use suggested").</summary>
    [ObservableProperty]
    private bool _canUseSuggestedThreshold;

    /// <summary>Thresholds closer than this count as equal (the field shows three decimals).</summary>
    private const double ThresholdTolerance = 5e-4;

    /// <summary>Moves the threshold to the suggestion (after a manual override).</summary>
    [RelayCommand]
    private void UseSuggestedThreshold()
    {
        if (SuggestedThreshold is { } suggested)
            Threshold = suggested;
    }

    partial void OnThresholdChanged(double value) => ReclassifyRows();

    partial void OnSelectedFunctionChanged(GateFunctionOption value)
    {
        RefreshDefaultGateName();
        if (!HasResult)
            return;
        ApplyFunctionExpectations();
        ApplySuggestedThreshold();
    }

    /// <summary>Marks each row with the bit the intended function expects (null for custom).</summary>
    private void ApplyFunctionExpectations()
    {
        var outputs = Math.Max(1, _table?.OutputPinNames.Count ?? 1);
        var inputs = Math.Max(1, _table?.InputPinNames.Count ?? 1);
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].ExpectedBit = GateFunctionTable.Expected(SelectedFunction.Function, i / outputs, inputs);
    }

    /// <summary>Computes the suggestion, shows margin/overlap and moves the threshold onto it.</summary>
    private void ApplySuggestedThreshold()
    {
        var samples = Rows.Select(r => new GatePowerSample(r.Power, r.ExpectedBit)).ToList();
        var suggestion = _advisor.Suggest(samples);
        SuggestedThreshold = suggestion.Threshold;
        HasOverlap = suggestion.LevelsOverlap;
        SuggestionText = suggestion.LevelsOverlap
            ? string.Format(CultureInfo.InvariantCulture, Translate("MakeGate.Overlap"),
                suggestion.BrightestZero, suggestion.DarkestOne)
            : string.Format(CultureInfo.InvariantCulture, Translate("MakeGate.Suggestion"),
                suggestion.Threshold, suggestion.MarginDb);
        Threshold = suggestion.Threshold;
        ReclassifyRows();
    }

    private void ClearThresholdSuggestion()
    {
        SuggestedThreshold = null;
        HasOverlap = false;
        SuggestionText = "";
        MatchText = "";
        MatchesFunction = false;
    }

    /// <summary>Re-reads every row against the current threshold and updates the match summary.</summary>
    private void ReclassifyRows()
    {
        foreach (var row in Rows)
            row.Classify(Threshold);
        CanUseSuggestedThreshold = SuggestedThreshold is { } suggested
            && Math.Abs(suggested - Threshold) > ThresholdTolerance;
        if (Rows.Count == 0)
            return;
        var mismatches = Rows.Count(r => !r.Matches);
        MatchesFunction = SelectedFunction.Function != GateFunction.Custom && mismatches == 0;
        MatchText = SelectedFunction.Function == GateFunction.Custom
            ? ""
            : mismatches == 0
                ? string.Format(Translate("MakeGate.Matches"), SelectedFunction.DisplayName)
                : string.Format(Translate("MakeGate.Mismatches"), mismatches, SelectedFunction.DisplayName);
    }

    /// <summary>Keeps an untouched default gate name in step with the picked function.</summary>
    private void RefreshDefaultGateName()
    {
        if (_group == null || !IsActive)
            return;
        var isPlaceholder = _group.GroupName.StartsWith(AutoGroupNamePrefix, StringComparison.Ordinal);
        var looksDefault = GateName.Length == 0
            || GateOptionNames.Contains(GateName.TrimEnd("0123456789".ToCharArray()));
        if (isPlaceholder && looksDefault)
            GateName = DefaultGateName();
    }

    /// <summary>Every name the default-name logic can produce (without the uniqueness suffix).</summary>
    private static IEnumerable<string> GateOptionNames => GateFunctionOption.All
        .Select(o => o.GateNameStem ?? Translate("MakeGate.DefaultGateName"));
}
