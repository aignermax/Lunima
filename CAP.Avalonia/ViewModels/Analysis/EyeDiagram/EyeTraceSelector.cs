using System.Globalization;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using CAP.Avalonia.Services.Localization;

namespace CAP.Avalonia.ViewModels.Analysis.EyeDiagram;

/// <summary>
/// Picks the trace the eye/BER analysis should evaluate (#690, #754). A user-designated
/// output coupler always wins; otherwise a coupler with its laser switched off is the
/// design's output. When several off couplers compete the strongest trace is used with
/// an explicit warning to pick one; when every coupler is still emitting (legacy design)
/// the strongest trace is used as a fallback, warning that the choice is arbitrary.
/// </summary>
internal static class EyeTraceSelector
{
    /// <summary>Warning shown when no coupler has its laser switched off.</summary>
    public const string AllLasersOnWarning =
        "All couplers are emitting — turn the laser off on your output coupler(s) for a meaningful eye.";

    /// <summary>Error shown when the coupler(s) with the laser off receive no light.</summary>
    public const string NoSignalAtOutputError =
        "No light from the switched-on input(s) arrives at the coupler(s) with the laser switched off — check the connection path from your input coupler.";

    /// <summary>
    /// Minimum output power relative to the injected peak power (−60 dB) for an output to
    /// count as receiving signal. The multi-hop closure produces numerically tiny traces at
    /// every reachable pin (spurious reflection/crosstalk terms of component S-matrices);
    /// evaluating an eye on such residue yields garbage Q/BER (#762 review, field round 4).
    /// </summary>
    public const double MinimumRelativeOutputPower = 1e-6;

    /// <summary>The power threshold in dB ("-60"), named in the gate message.</summary>
    private static string MinimumRelativeOutputPowerDb =>
        (10 * Math.Log10(MinimumRelativeOutputPower)).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Error shown when the user-designated output coupler receives no light (#754).</summary>
    public static string NoSignalAtDesignatedOutputError =>
        LocalizationService.Instance.Translate("Analysis.Output.NoSignalDesignated");

    /// <summary>
    /// Error shown when the designated output receives only sub-threshold light — likely a
    /// spurious reflection, not a signal path (#762 review). Names the −60 dB threshold.
    /// </summary>
    public static string BelowNoiseFloorError =>
        string.Format(
            LocalizationService.Instance.Translate("Analysis.Output.BelowNoiseFloor"),
            MinimumRelativeOutputPowerDb);

    /// <summary>
    /// Warning shown when several off couplers compete and none is designated (#754):
    /// the strongest trace is evaluated, but the user should pick the output explicitly.
    /// </summary>
    public static string MultipleOutputsWarning =>
        LocalizationService.Instance.Translate("Analysis.Output.MultipleCandidatesWarning");

    /// <summary>
    /// Outcome of the selection: either a <see cref="Trace"/> (possibly with a
    /// non-fatal <see cref="Warning"/>) or a user-facing <see cref="Error"/>.
    /// </summary>
    public sealed record Selection(double[]? Trace, string? Warning, string? Error);

    /// <summary>
    /// Selects the analysis trace from a completed transient result.
    /// </summary>
    /// <param name="result">Transient result with at least one pin trace.</param>
    /// <param name="outputCouplerPinIds">
    /// Flow ids of the light pins on couplers whose laser is off (true outputs).
    /// Empty when every laser is on.
    /// </param>
    /// <param name="designatedPinIds">
    /// Flow ids of the light pins on the user-designated output coupler (#754), or
    /// null when no coupler is designated. When set, only these pins are evaluated.
    /// </param>
    /// <param name="hasMultipleCandidates">
    /// True when several off couplers compete without a designation, so the fallback
    /// choice gets an explicit warning instead of guessing silently (#754).
    /// </param>
    /// <param name="injectedPeakPower">
    /// Peak power injected at the strongest active input. When positive, an output only
    /// counts as receiving signal when its peak intensity reaches
    /// <see cref="MinimumRelativeOutputPower"/> (−60 dB) of this value — sub-threshold
    /// residue from the multi-hop closure is treated as "no signal" (#762 review). Zero
    /// (the default) disables the power gate.
    /// </param>
    public static Selection Select(
        TimeDomainResult result,
        IReadOnlyCollection<Guid> outputCouplerPinIds,
        IReadOnlyCollection<Guid>? designatedPinIds = null,
        bool hasMultipleCandidates = false,
        double injectedPeakPower = 0)
    {
        var selected = CAP_Core.Analysis.EyeDiagram.EyeTracePolicy.Select(result, outputCouplerPinIds,
            designatedPinIds, hasMultipleCandidates, injectedPeakPower,
            new(NoSignalAtDesignatedOutputError, BelowNoiseFloorError, MultipleOutputsWarning));
        return new(selected.Trace, selected.Warning, selected.Error);
    }
}
