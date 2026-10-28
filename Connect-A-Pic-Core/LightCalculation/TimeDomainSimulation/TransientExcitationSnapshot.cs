using System.Collections.Immutable;
using CAP_Core.LightCalculation.TimeDomainSimulation.PhaseNoise;

namespace CAP_Core.LightCalculation.TimeDomainSimulation;

public readonly record struct TransientSource(Guid FlowId, double Amplitude);

/// <summary>Ordered laser inputs and receiver context, detached from ports and editor settings.</summary>
public sealed class TransientExcitationSnapshot
{
    public ImmutableArray<TransientSource> Sources { get; }
    public ImmutableHashSet<Guid> OutputPinIds { get; }
    public double RinDbPerHz { get; }
    private readonly ImmutableArray<KeyValuePair<Guid, PhaseNoiseSource>> _phaseSources;
    private readonly int _phaseSeed;

    public TransientExcitationSnapshot(IEnumerable<TransientSource> sources, PhaseNoiseSettings phaseNoise,
        IEnumerable<Guid> outputPinIds, double rinDbPerHz)
    {
        Sources = sources.ToImmutableArray();
        OutputPinIds = outputPinIds.ToImmutableHashSet();
        RinDbPerHz = rinDbPerHz;
        _phaseSeed = phaseNoise.Seed;
        _phaseSources = phaseNoise.Sources.Select(pair => new KeyValuePair<Guid, PhaseNoiseSource>(pair.Key,
            new(pair.Value.LinewidthHz, pair.Value.LockGroupId, pair.Value.PhaseOffsetRad))).ToImmutableArray();
    }

    public PhaseNoiseSettings CreatePhaseNoise()
    {
        var result = new PhaseNoiseSettings(_phaseSeed);
        foreach (var source in _phaseSources) result.AddSource(source.Key, source.Value);
        return result;
    }
}

internal sealed class TransientWarningCollector
{
    private readonly HashSet<string> _names = new();
    private readonly List<PassivityWarning> _warnings = new();
    internal void Add(PassivityWarning warning)
    {
        if (_names.Add(warning.ComponentName)) _warnings.Add(warning);
    }
    internal ImmutableArray<PassivityWarning> ToImmutable() => _warnings.ToImmutableArray();
}
