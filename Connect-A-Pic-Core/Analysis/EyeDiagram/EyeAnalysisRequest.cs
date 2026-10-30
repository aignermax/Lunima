using System.Collections.Immutable;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.TimeDomainSimulation;

namespace CAP_Core.Analysis.EyeDiagram;

public sealed record EyeAnalysisOutcome(EyeHistogram? Histogram, EyeMetrics? Metrics, string? Error,
    string? Warning, ImmutableArray<PassivityWarning> PassivityWarnings);

/// <summary>Detached PRBS, optical and receiver inputs for one eye calculation.</summary>
public sealed record EyeAnalysisRequest(TransientCircuitSnapshot Circuit, TransientExcitationSnapshot Excitation,
    double BitRateHz, PrbsOrder PrbsOrder, double ThresholdRelative, ImmutableHashSet<Guid>? DesignatedPinIds,
    bool HasMultipleCandidates, EyeTraceMessages Messages)
{
    public EyeAnalysisOutcome Solve(CancellationToken cancellationToken = default,
        Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? decorateBuilder = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warnings = new TransientWarningCollector();
        var simulator = Circuit.CreateSimulator(warnings.Add, decorateBuilder);
        const double center = TimeDomainSimulator.DefaultCenterWavelengthNm;
        const double span = TimeDomainSimulator.DefaultSpanNm;
        const int frequencyPoints = TimeDomainSimulator.DefaultNPoints;
        var sweep = TimeSignalDefinition.FromWavelengthSweep(center, span, frequencyPoints);
        var plan = EyeSimulationPlan.Create(BitRateHz, sweep.SampleRateHz, PrbsGenerator.PatternLength(PrbsOrder));
        var bits = PrbsGenerator.GenerateBits(PrbsOrder, plan.BitCount);
        var time = new TimeSignalDefinition(sweep.SampleRateHz, plan.TotalSamples);
        var signals = new Dictionary<Guid, double[]>();
        double peakPower = 0;
        foreach (var source in Excitation.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            peakPower = Math.Max(peakPower, source.Amplitude * source.Amplitude);
            signals[source.FlowId] = PrbsGenerator.ToNrzSamples(bits, plan.SamplesPerBit, source.Amplitude);
        }
        EyeAnalysisOutcome Error(string? message) => new(null, null, message, null, warnings.ToImmutable());
        if (signals.Count == 0)
            return Error(Excitation.OutputPinIds.Count > 0
                ? "No laser is switched on \u2014 turn the laser on at your input coupler."
                : "No light source found \u2014 place an input coupler (e.g. a grating/edge coupler).");
        var result = simulator.Run(signals, time, center, span, frequencyPoints, Excitation.CreatePhaseNoise(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.PinTraces.Count == 0)
            return Error("The circuit produced no output traces \u2014 connect an output path from the light source to a detector/output.");
        var selection = EyeTracePolicy.Select(result, Excitation.OutputPinIds, DesignatedPinIds,
            HasMultipleCandidates, peakPower, Messages);
        if (selection.Trace == null) return Error(selection.Error);
        var noise = new NoiseModel { BandwidthHz = 0.75 * BitRateHz, RinDbPerHz = Excitation.RinDbPerHz };
        var plotTrace = ReceiverNoiseTrace.Apply(selection.Trace, noise);
        var timeBins = Math.Min(EyeDiagramBuilder.DefaultTimeBins, plan.SamplesPerBit);
        var histogram = EyeDiagramBuilder.Build(plotTrace, time.SampleRateHz, plan.BitPeriodSeconds, timeBins);
        var threshold = histogram.MinAmplitude + ThresholdRelative * (histogram.MaxAmplitude - histogram.MinAmplitude);
        var metrics = BerEstimator.Estimate(selection.Trace, time.SampleRateHz, plan.BitPeriodSeconds, threshold, noise, timeBins);
        cancellationToken.ThrowIfCancellationRequested();
        return new(histogram, metrics, null, selection.Warning, warnings.ToImmutable());
    }
}

public interface IEyeAnalysisRunner
{
    Task<EyeAnalysisOutcome> RunAsync(EyeAnalysisRequest request, CancellationToken cancellationToken);
}

public sealed class EyeAnalysisRunner : IEyeAnalysisRunner
{
    public Task<EyeAnalysisOutcome> RunAsync(EyeAnalysisRequest request, CancellationToken cancellationToken)
        => Task.Run(() => request.Solve(cancellationToken), cancellationToken);
}
