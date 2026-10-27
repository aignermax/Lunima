using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.TimeDomainSimulation;

namespace UnitTests.Analysis.Snapshots;

/// <summary>
/// Frozen pre-snapshot Monte Carlo eye calculation. In particular, its threshold uses
/// the noiseless histogram and the receiver keeps NoiseModel's default RIN, independently
/// of the input laser's configured RIN. This is an oracle, not a second production engine.
/// </summary>
internal static class MonteCarloLegacyEyeOracle
{
    internal static Task<double[]> SampleAsync(DesignCanvasViewModel canvas, AnalysisOutputResolution output,
        Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? decorate = null)
        => Task.Run(() => new[] { Calculate(canvas, output, decorate).EyeHeight });

    internal static Task<Observation> ObserveAsync(DesignCanvasViewModel canvas, AnalysisOutputResolution output)
        => Task.Run(() => Calculate(canvas, output, null));

    internal sealed record Observation(double EyeHeight, IReadOnlyDictionary<Guid, double> TracePeaks,
        IReadOnlyList<Guid> CandidateIds, double SelectedPeak);

    private static Observation Calculate(DesignCanvasViewModel canvas, AnalysisOutputResolution output,
        Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? decorate)
    {
        const double bitRate = 25e9;
        var (simulator, ports) = TransientCircuitFactory.Create(canvas, decorateBuilder: decorate);
        var outputIds = TransientCircuitFactory.CollectOutputCouplerPinIds(canvas);
        var designated = output.State == AnalysisOutputState.DesignatedValid
            ? AnalysisOutputResolver.CollectLightPinIds(output.Output!) : null;
        var sweep = TimeSignalDefinition.FromWavelengthSweep(TimeDomainSimulator.DefaultCenterWavelengthNm,
            TimeDomainSimulator.DefaultSpanNm, TimeDomainSimulator.DefaultNPoints);
        var plan = EyeSimulationPlan.Create(bitRate, sweep.SampleRateHz, PrbsGenerator.PatternLength(PrbsOrder.Prbs7));
        var bits = PrbsGenerator.GenerateBits(PrbsOrder.Prbs7, plan.BitCount);
        var time = new TimeSignalDefinition(sweep.SampleRateHz, plan.TotalSamples);
        var signals = new Dictionary<Guid, double[]>();
        double peak = 0;
        foreach (var source in ports.GetUsedExternalInputs())
        {
            var amplitude = Math.Sqrt(source.Input.InFlowPower.Magnitude);
            peak = Math.Max(peak, amplitude * amplitude);
            signals[source.AttachedComponentPinId] = PrbsGenerator.ToNrzSamples(bits, plan.SamplesPerBit, amplitude);
        }
        if (signals.Count == 0) throw new InvalidOperationException("Legacy oracle requires an enabled source.");
        var result = simulator.Run(signals, time, TimeDomainSimulator.DefaultCenterWavelengthNm,
            TimeDomainSimulator.DefaultSpanNm, TimeDomainSimulator.DefaultNPoints,
            TransientCircuitFactory.BuildPhaseNoiseSettings(canvas));
        var selected = EyeTraceSelector.Select(result, outputIds, designated,
            output.State == AnalysisOutputState.MultipleCandidates, peak);
        if (selected.Trace == null) throw new InvalidOperationException(selected.Error);
        var bins = Math.Min(EyeDiagramBuilder.DefaultTimeBins, plan.SamplesPerBit);
        var histogram = EyeDiagramBuilder.Build(selected.Trace, time.SampleRateHz, plan.BitPeriodSeconds, bins);
        var threshold = histogram.MinAmplitude + 0.5 * (histogram.MaxAmplitude - histogram.MinAmplitude);
        var noise = new NoiseModel { BandwidthHz = 0.75 * bitRate };
        var height = BerEstimator.Estimate(selected.Trace, time.SampleRateHz, plan.BitPeriodSeconds,
            threshold, noise, bins).EyeHeight;
        var candidates = designated ?? outputIds;
        return new(height, result.PinTraces.ToDictionary(pair => pair.Key, pair => pair.Value.Max()),
            result.PinTraces.Keys.Where(id => candidates.Count == 0 || candidates.Contains(id)).ToArray(),
            selected.Trace.Max());
    }
}
