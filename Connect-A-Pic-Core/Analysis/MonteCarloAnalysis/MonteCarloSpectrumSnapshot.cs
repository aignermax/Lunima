using CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using System.Collections.Immutable;

namespace CAP_Core.Analysis.MonteCarloAnalysis;

/// <summary>Input boundary for one nominal spectrum and its seeded fabrication samples.</summary>
public sealed class MonteCarloSpectrumSnapshot
{
    private readonly MonteCarloConfiguration _configuration;
    private readonly WavelengthSweepSnapshot _sweep;
    private readonly FabricationSnapshot _fabrication;
    private readonly ImmutableHashSet<Guid> _preferredOutputs;

    private MonteCarloSpectrumSnapshot(MonteCarloConfiguration configuration, WavelengthSweepConfiguration sweep,
        GridManager grid, IExternalPortManager ports, IReadOnlyList<Component> participants,
        ProcessTolerances tolerances, IReadOnlySet<Guid> preferredOutputs)
    {
        _configuration = new(configuration.RunCount, configuration.Seed);
        _sweep = WavelengthSweepSnapshot.Capture(sweep, grid, ports);
        _fabrication = new(participants, tolerances);
        _preferredOutputs = preferredOutputs.ToImmutableHashSet();
    }

    /// <summary>Captures the input boundary before scheduling worker execution.</summary>
    public static MonteCarloSpectrumSnapshot Capture(MonteCarloConfiguration configuration,
        WavelengthSweepConfiguration sweep, GridManager grid, IExternalPortManager ports,
        IReadOnlyList<Component> participants, ProcessTolerances tolerances, IReadOnlySet<Guid> preferredOutputs)
        => new(configuration, sweep, grid, ports, participants, tolerances, preferredOutputs);

    /// <summary>Runs nominal first, selects its output once, then evaluates sequential seeded samples.</summary>
    public async Task<MonteCarloSpectrumResult> SolveAsync(IProgress<MonteCarloProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _fabrication.CreateRun(cancellationToken);
        Guid? selected = null;
        async Task<double[]> Sample(CancellationToken token)
        {
            var result = await _sweep.SolveAsync(token, source.Decorate).ConfigureAwait(false);
            if (selected == null)
            {
                var candidates = result.MonitoredPinIds.Where(_preferredOutputs.Contains).ToList();
                if (candidates.Count == 0) candidates = result.MonitoredPinIds.ToList();
                selected = candidates.OrderByDescending(pin => result.GetInsertionLossSeriesForPin(pin).Average()).First();
            }
            return result.GetInsertionLossSeriesForPin(selected.Value);
        }
        var samples = await new MonteCarloRunner().RunAsync(_configuration, source, Sample, progress, cancellationToken)
            .ConfigureAwait(false);
        return new(samples, selected!.Value);
    }
}

/// <summary>Fabrication samples and the single output selected from the nominal spectrum.</summary>
public sealed record MonteCarloSpectrumResult(MonteCarloResult Samples, Guid SelectedOutputPinId);
