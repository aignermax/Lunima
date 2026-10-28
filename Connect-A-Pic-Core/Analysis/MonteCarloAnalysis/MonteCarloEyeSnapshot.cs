using CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;

namespace CAP_Core.Analysis.MonteCarloAnalysis;

/// <summary>Captured transient metric plus ordered fabrication inputs for a complete seeded eye run.</summary>
public sealed class MonteCarloEyeSnapshot
{
    private readonly MonteCarloConfiguration _configuration;
    private readonly MonteCarloEyeMetric _metric;
    private readonly FabricationSnapshot _fabrication;

    private MonteCarloEyeSnapshot(MonteCarloConfiguration configuration, MonteCarloEyeMetric metric,
        IReadOnlyList<Component> participants, ProcessTolerances tolerances)
    {
        _configuration = new(configuration.RunCount, configuration.Seed);
        _metric = metric;
        _fabrication = new(participants, tolerances);
    }

    public static MonteCarloEyeSnapshot Capture(MonteCarloConfiguration configuration, MonteCarloEyeMetric metric,
        IReadOnlyList<Component> participants, ProcessTolerances tolerances) => new(configuration, metric, participants, tolerances);

    /// <summary>Each invocation owns its variance state, restored by the shared runner on every exit.</summary>
    public Task<MonteCarloResult> SolveAsync(IProgress<MonteCarloProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _fabrication.CreateRun(cancellationToken);
        return new MonteCarloRunner().RunAsync(_configuration, source,
            token => Task.FromResult(new[] { _metric.Solve(token, source.Decorate) }), progress, cancellationToken);
    }
}
