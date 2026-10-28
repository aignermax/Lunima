using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis.CircuitOptimization
{
    /// <summary>
    /// Budget-limited, seeded hill-climb over component slider parameters.
    /// Perturbs one random parameter at a time around the best point found so far,
    /// shrinking the step on non-improving moves, and reports the top-N variants
    /// that beat the baseline design. Slider values are always restored afterwards.
    /// </summary>
    public class CircuitOptimizer
    {
        private readonly ILightCalculator _calculator;

        /// <summary>Creates an optimizer that evaluates designs with the given calculator.</summary>
        public CircuitOptimizer(ILightCalculator calculator)
        {
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        /// <summary>
        /// Runs the search. Cancellation stops the run cleanly: partial results are
        /// returned with <see cref="OptimizationResult.WasCancelled"/> set, and the
        /// original slider values are restored either way.
        /// </summary>
        public async Task<OptimizationResult> RunAsync(
            OptimizationSettings settings,
            CancellationToken cancellationToken = default,
            IProgress<OptimizationProgress>? progress = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var originalValues = settings.Parameters.Select(p => p.GetSlider().Value).ToArray();
            try
            {
                var ranges = settings.Parameters.Select(p => new OptimizationSearch.Range(p.MinValue, p.MaxValue)).ToArray();
                var result = await OptimizationSearch.RunAsync(ranges, originalValues, settings.EvaluationBudget,
                    settings.Seed, settings.TopN, (values, token) => EvaluateAsync(settings, values, token),
                    cancellationToken, progress);
                return new OptimizationResult(settings, result.BaselineScore, result.TopVariants,
                    result.EvaluationsUsed, result.WasCancelled);
            }
            finally
            {
                ApplyValues(settings, originalValues);
            }
        }

        private async Task<double> EvaluateAsync(
            OptimizationSettings settings,
            double[] values,
            CancellationToken cancellationToken)
        {
            ApplyValues(settings, values);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var fieldResults = await _calculator.CalculateFieldPropagationAsync(cts, settings.WavelengthNm);

            var powers = new Dictionary<Guid, double>(fieldResults.Count);
            foreach (var kvp in fieldResults)
                powers[kvp.Key] = kvp.Value.Magnitude * kvp.Value.Magnitude;

            return settings.Objective.Score(powers);
        }

        private static void ApplyValues(OptimizationSettings settings, double[] values)
        {
            for (int i = 0; i < settings.Parameters.Count; i++)
                settings.Parameters[i].GetSlider().Value = values[i];
        }

    }
}
