using System.Collections.Immutable;
using CAP_Core.LightCalculation.TimeDomainSimulation.Sampling;
using CAP_Core.LightCalculation.TimeDomainSimulation.Sources;

namespace CAP_Core.LightCalculation.TimeDomainSimulation;

public enum TransientWaveform { GaussianPulse, ContinuousWave, PrbsNrz }

public sealed record TransientWaveformSettings(TransientWaveform Kind, double BitrateHz, int PrbsOrder,
    int SamplesPerSymbol, int SymbolCount, double ExtinctionRatioDb, int Seed,
    double PulseCenterSeconds, double PulseSigmaSeconds)
{
    internal TimeSignalDefinition CreateGrid(double centerNm, double spanNm, int frequencyPoints)
        => Kind == TransientWaveform.PrbsNrz
            ? SamplingPolicy.CreateGrid(BitrateHz, SamplesPerSymbol, SymbolCount, frequencyPoints)
            : TimeSignalDefinition.FromWavelengthSweep(centerNm, spanNm, frequencyPoints);

    internal ISignalSource CreateSource(double amplitude) => Kind switch
    {
        TransientWaveform.ContinuousWave => new CwSource(amplitude),
        TransientWaveform.PrbsNrz => new PrbsSource(BitrateHz, PrbsOrder, amplitude, ExtinctionRatioDb, Seed),
        _ => new PulseSource(Math.Max(PulseCenterSeconds, 3 * PulseSigmaSeconds), PulseSigmaSeconds, amplitude)
    };
}

public sealed record TransientSimulationOutcome(TimeDomainResult Result, ImmutableArray<PassivityWarning> Warnings);

/// <summary>Complete transient input. Sample arrays and mutable matrices are created only by the worker.</summary>
public sealed record TransientSimulationRequest(TransientCircuitSnapshot Circuit, TransientExcitationSnapshot Excitation,
    TransientWaveformSettings Waveform, double CenterWavelengthNm, double SpanNm, int FrequencyPoints)
{
    public TransientSimulationOutcome Solve(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warnings = new TransientWarningCollector();
        var simulator = Circuit.CreateSimulator(warnings.Add);
        var grid = Waveform.CreateGrid(CenterWavelengthNm, SpanNm, FrequencyPoints);
        var signals = new Dictionary<Guid, double[]>();
        foreach (var source in Excitation.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            signals[source.FlowId] = Waveform.CreateSource(source.Amplitude).Generate(grid);
        }
        var result = simulator.Run(signals, grid, CenterWavelengthNm, SpanNm, FrequencyPoints,
            Excitation.CreatePhaseNoise(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, warnings.ToImmutable());
    }
}

public interface ITransientSimulationRunner
{
    Task<TransientSimulationOutcome> RunAsync(TransientSimulationRequest request, CancellationToken cancellationToken);
}

public sealed class TransientSimulationRunner : ITransientSimulationRunner
{
    public Task<TransientSimulationOutcome> RunAsync(TransientSimulationRequest request, CancellationToken cancellationToken)
        => Task.Run(() => request.Solve(cancellationToken), cancellationToken);
}
