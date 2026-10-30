using CAP_Core.LightCalculation.TimeDomainSimulation;

namespace CAP_Core.Analysis.EyeDiagram;

public sealed record EyeTraceMessages(string NoDesignatedSignal, string BelowNoiseFloor, string MultipleOutputs);
public sealed record EyeTraceSelection(double[]? Trace, string? Warning, string? Error);

/// <summary>Deterministic output selection; localized messages are captured by the caller.</summary>
public static class EyeTracePolicy
{
    public const double MinimumRelativeOutputPower = 1e-6;
    public const string AllLasersOnWarning =
        "All couplers are emitting — turn the laser off on your output coupler(s) for a meaningful eye.";
    public const string NoSignalAtOutputError =
        "No light from the switched-on input(s) arrives at the coupler(s) with the laser switched off — check the connection path from your input coupler.";

    public static EyeTraceSelection Select(TimeDomainResult result, IReadOnlyCollection<Guid> outputs,
        IReadOnlyCollection<Guid>? designated, bool multiple, double injectedPeakPower, EyeTraceMessages messages)
    {
        var threshold = injectedPeakPower * MinimumRelativeOutputPower;
        if (designated != null)
        {
            var traces = TracesFor(result, designated);
            if (traces.Count == 0) return new(null, null, messages.NoDesignatedSignal);
            var strongest = Strongest(traces);
            return Peak(strongest) < threshold ? new(null, null, messages.BelowNoiseFloor) : new(strongest, null, null);
        }
        if (outputs.Count == 0) return new(Strongest(result.PinTraces.Values), AllLasersOnWarning, null);
        var candidates = TracesFor(result, outputs);
        if (candidates.Count == 0) return new(null, null, NoSignalAtOutputError);
        var candidate = Strongest(candidates);
        return Peak(candidate) < threshold ? new(null, null, NoSignalAtOutputError)
            : new(candidate, multiple ? messages.MultipleOutputs : null, null);
    }

    private static List<double[]> TracesFor(TimeDomainResult result, IReadOnlyCollection<Guid> ids)
        => result.PinTraces.Where(pair => ids.Contains(pair.Key)).Select(pair => pair.Value).ToList();
    private static double[] Strongest(IEnumerable<double[]> traces) => traces.OrderByDescending(Peak).First();
    private static double Peak(double[] trace) => trace.Length == 0 ? 0 : trace.Max();
}
