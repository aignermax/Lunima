namespace CAP_Core.Analysis
{
    /// <summary>
    /// Aggregated results from a stochastic simulation run.
    /// </summary>
    public class StochasticResult
    {
        /// <summary>
        /// Number of Monte Carlo iterations performed.
        /// </summary>
        public int IterationCount { get; }

        /// <summary>
        /// The parameter variations that were applied.
        /// </summary>
        public IReadOnlyList<ParameterVariation> Variations { get; }

        /// <summary>
        /// Per-pin output power statistics.
        /// </summary>
        public IReadOnlyList<OutputPowerStatistics> PinStatistics { get; }

        /// <summary>
        /// Creates a new stochastic simulation result.
        /// </summary>
        /// <param name="iterationCount">Number of iterations.</param>
        /// <param name="variations">Applied parameter variations.</param>
        /// <param name="pinStatistics">Per-pin statistics.</param>
        public StochasticResult(
            int iterationCount,
            IReadOnlyList<ParameterVariation> variations,
            IReadOnlyList<OutputPowerStatistics> pinStatistics)
        {
            IterationCount = iterationCount;
            Variations = variations;
            PinStatistics = pinStatistics;
        }

        /// <summary>
        /// Generates a histogram of output power distribution for a given pin.
        /// </summary>
        /// <param name="pinId">The pin to generate a histogram for.</param>
        /// <param name="bucketCount">Number of histogram bins.</param>
        /// <returns>A list of histogram buckets, or empty if pin not found.</returns>
        public List<HistogramBucket> GetHistogram(
            Guid pinId,
            int bucketCount = HistogramGenerator.DefaultBucketCount)
        {
            var stats = PinStatistics.FirstOrDefault(s => s.PinId == pinId);
            if (stats == null)
                return new List<HistogramBucket>();

            return HistogramGenerator.Generate(stats.Samples, bucketCount);
        }
    }
}
