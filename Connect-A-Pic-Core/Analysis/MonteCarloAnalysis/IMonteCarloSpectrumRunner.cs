namespace CAP_Core.Analysis.MonteCarloAnalysis;

/// <summary>Executes a captured fabrication spectrum without retaining the caller's design.</summary>
public interface IMonteCarloSpectrumRunner
{
    Task<MonteCarloSpectrumResult> RunAsync(MonteCarloSpectrumSnapshot snapshot,
        IProgress<MonteCarloProgress>? progress, CancellationToken cancellationToken);
}

public class MonteCarloSpectrumRunner : IMonteCarloSpectrumRunner
{
    public virtual Task<MonteCarloSpectrumResult> RunAsync(MonteCarloSpectrumSnapshot snapshot,
        IProgress<MonteCarloProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => snapshot.SolveAsync(progress, cancellationToken), cancellationToken);
}
