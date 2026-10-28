namespace CAP_Core.Analysis.CircuitOptimization;

public interface ICircuitOptimizationRunner
{
    Task<CircuitOptimizationRunResult> RunAsync(CircuitOptimizationSnapshot snapshot,
        CancellationToken cancellationToken, IProgress<OptimizationProgress>? progress = null);
}

public class CircuitOptimizationRunner : ICircuitOptimizationRunner
{
    public virtual Task<CircuitOptimizationRunResult> RunAsync(CircuitOptimizationSnapshot snapshot,
        CancellationToken cancellationToken, IProgress<OptimizationProgress>? progress = null) =>
        Task.Run(() => snapshot.SolveAsync(cancellationToken, progress), cancellationToken);
}
