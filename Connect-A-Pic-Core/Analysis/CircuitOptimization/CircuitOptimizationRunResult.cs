using System.Collections.Immutable;

namespace CAP_Core.Analysis.CircuitOptimization;

/// <summary>Owned search results without a reference to the live parameter settings.</summary>
public sealed class CircuitOptimizationRunResult
{
    public double BaselineScore { get; }
    public IReadOnlyList<OptimizationCandidate> TopVariants { get; }
    public int EvaluationsUsed { get; }
    public bool WasCancelled { get; }

    public CircuitOptimizationRunResult(double baselineScore, IEnumerable<OptimizationCandidate> topVariants,
        int evaluationsUsed, bool wasCancelled)
    {
        BaselineScore = baselineScore;
        TopVariants = topVariants.Select(value =>
            new OptimizationCandidate(value.ParameterValues.ToImmutableArray(), value.Score)).ToImmutableArray();
        EvaluationsUsed = evaluationsUsed;
        WasCancelled = wasCancelled;
    }
}
