namespace CAP_Core.Analysis.MonteCarloAnalysis;

/// <summary>Runs only captured transient and fabrication inputs on a worker.</summary>
public interface IMonteCarloEyeRunner
{
    Task<MonteCarloResult> RunAsync(MonteCarloEyeSnapshot snapshot,
        IProgress<MonteCarloProgress>? progress, CancellationToken cancellationToken);
}

public class MonteCarloEyeRunner : IMonteCarloEyeRunner
{
    public virtual Task<MonteCarloResult> RunAsync(MonteCarloEyeSnapshot snapshot,
        IProgress<MonteCarloProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => snapshot.SolveAsync(progress, cancellationToken), cancellationToken);
}
