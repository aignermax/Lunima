namespace CAP_Core.Analysis.CircuitOptimization;

/// <summary>The shared seeded hill-climb; evaluation and mutable model ownership belong to its caller.</summary>
internal static class OptimizationSearch
{
    internal readonly record struct Range(double MinValue, double MaxValue);
    internal sealed record Result(double BaselineScore, List<OptimizationCandidate> TopVariants,
        int EvaluationsUsed, bool WasCancelled);

    internal static async Task<Result> RunAsync(IReadOnlyList<Range> parameters, double[] startValues,
        int budget, int seed, int topN, Func<double[], CancellationToken, Task<double>> evaluate,
        CancellationToken cancellationToken, IProgress<OptimizationProgress>? progress)
    {
        var rng = new Random(seed);
        var evaluated = new List<OptimizationCandidate>();
        double stepFraction = OptimizationSettings.InitialStepFraction;
        int evaluations = 0;
        bool cancelled = false;
        double baselineScore = double.NaN;
        var bestValues = (double[])startValues.Clone();
        double bestScore = double.NegativeInfinity;
        try
        {
            baselineScore = await evaluate(startValues, cancellationToken);
            evaluations++;
            bestScore = baselineScore;
            progress?.Report(new OptimizationProgress(evaluations, budget, bestScore));
            while (evaluations < budget)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidateValues = Mutate(parameters, bestValues, rng, stepFraction);
                double score = await evaluate(candidateValues, cancellationToken);
                evaluations++;
                evaluated.Add(new OptimizationCandidate(candidateValues, score));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestValues = candidateValues;
                    stepFraction = OptimizationSettings.InitialStepFraction;
                }
                else
                {
                    stepFraction = Math.Max(OptimizationSettings.MinStepFraction,
                        stepFraction * OptimizationSettings.StepDecayFactor);
                }
                progress?.Report(new OptimizationProgress(evaluations, budget, bestScore));
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        var top = double.IsNaN(baselineScore) ? new List<OptimizationCandidate>() : evaluated
            .Where(c => c.Score > baselineScore).OrderByDescending(c => c.Score)
            .DistinctBy(c => string.Join(";", c.ParameterValues)).Take(topN).ToList();
        return new(baselineScore, top, evaluations, cancelled);
    }

    private static double[] Mutate(IReadOnlyList<Range> parameters, double[] baseValues,
        Random rng, double stepFraction)
    {
        var values = (double[])baseValues.Clone();
        int index = rng.Next(parameters.Count);
        var parameter = parameters[index];
        double range = parameter.MaxValue - parameter.MinValue;
        double delta = range * stepFraction * (2 * rng.NextDouble() - 1);
        values[index] = Math.Clamp(values[index] + delta, parameter.MinValue, parameter.MaxValue);
        return values;
    }
}
