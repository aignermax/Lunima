using System.Collections.Immutable;
using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.TimeDomainSimulation;

namespace CAP_Core.Analysis.MonteCarloAnalysis;

/// <summary>
/// The Monte Carlo eye-height policy over detached transient inputs. Its legacy receiver
/// uses a noiseless histogram for the decision threshold and NoiseModel's default RIN.
/// The eye panel's noisy display histogram and configured receiver RIN are a different policy.
/// </summary>
public sealed class MonteCarloEyeMetric
{
    private const double BitRateHz = 25e9;
    private readonly TransientCircuitSnapshot _circuit;
    private readonly TransientExcitationSnapshot _excitation;
    private readonly ImmutableHashSet<Guid>? _designated;
    private readonly bool _multiple;
    private readonly EyeTraceMessages _messages;
    private readonly string _noLaserMessage;

    public MonteCarloEyeMetric(TransientCircuitSnapshot circuit, TransientExcitationSnapshot excitation,
        IEnumerable<Guid>? designatedPinIds, bool hasMultipleCandidates, EyeTraceMessages messages,
        string noLaserMessage)
    {
        _circuit = circuit;
        _excitation = excitation;
        _designated = designatedPinIds?.ToImmutableHashSet();
        _multiple = hasMultipleCandidates;
        _messages = messages;
        _noLaserMessage = noLaserMessage;
    }

    /// <summary>Constructs fresh private matrices for this sample and its worker-owned decorator.</summary>
    public double Solve(CancellationToken cancellationToken = default,
        Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? decorateBuilder = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var simulator = _circuit.CreateSimulator(decorateBuilder: decorateBuilder);
        var sweep = TimeSignalDefinition.FromWavelengthSweep(TimeDomainSimulator.DefaultCenterWavelengthNm,
            TimeDomainSimulator.DefaultSpanNm, TimeDomainSimulator.DefaultNPoints);
        var plan = EyeSimulationPlan.Create(BitRateHz, sweep.SampleRateHz, PrbsGenerator.PatternLength(PrbsOrder.Prbs7));
        var bits = PrbsGenerator.GenerateBits(PrbsOrder.Prbs7, plan.BitCount);
        var time = new TimeSignalDefinition(sweep.SampleRateHz, plan.TotalSamples);
        var signals = new Dictionary<Guid, double[]>();
        double peak = 0;
        foreach (var source in _excitation.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            peak = Math.Max(peak, source.Amplitude * source.Amplitude);
            signals[source.FlowId] = PrbsGenerator.ToNrzSamples(bits, plan.SamplesPerBit, source.Amplitude);
        }
        if (signals.Count == 0) throw new InvalidOperationException(_noLaserMessage);
        var result = simulator.Run(signals, time, TimeDomainSimulator.DefaultCenterWavelengthNm,
            TimeDomainSimulator.DefaultSpanNm, TimeDomainSimulator.DefaultNPoints,
            _excitation.CreatePhaseNoise(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = EyeTracePolicy.Select(result, _excitation.OutputPinIds, _designated, _multiple, peak, _messages);
        if (selected.Trace == null) throw new InvalidOperationException(selected.Error);
        var height = EstimateEyeHeight(selected.Trace, time.SampleRateHz, plan);
        cancellationToken.ThrowIfCancellationRequested();
        return height;
    }

    private static double EstimateEyeHeight(double[] trace, double sampleRateHz, EyeSimulationPlan plan)
    {
        var bins = Math.Min(EyeDiagramBuilder.DefaultTimeBins, plan.SamplesPerBit);
        var histogram = EyeDiagramBuilder.Build(trace, sampleRateHz, plan.BitPeriodSeconds, bins);
        var threshold = histogram.MinAmplitude + 0.5 * (histogram.MaxAmplitude - histogram.MinAmplitude);
        var noise = new NoiseModel { BandwidthHz = 0.75 * BitRateHz };
        return BerEstimator.Estimate(trace, sampleRateHz, plan.BitPeriodSeconds, threshold, noise, bins).EyeHeight;
    }
}
