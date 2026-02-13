namespace CAP_Core.Analysis
{
    /// <summary>
    /// Statistical summary of output power at a single pin across stochastic runs.
    /// </summary>
    public class OutputPowerStatistics
    {
        /// <summary>
        /// The pin identifier (GUID) this statistics belongs to.
        /// </summary>
        public Guid PinId { get; }

        /// <summary>
        /// Mean output power across all simulation runs.
        /// </summary>
        public double MeanPower { get; }

        /// <summary>
        /// Standard deviation of output power across all simulation runs.
        /// </summary>
        public double StdDeviation { get; }

        /// <summary>
        /// All individual power samples from each simulation run.
        /// </summary>
        public IReadOnlyList<double> Samples { get; }

        /// <summary>
        /// Creates a new output power statistics summary.
        /// </summary>
        /// <param name="pinId">The pin identifier.</param>
        /// <param name="samples">All power samples for this pin.</param>
        public OutputPowerStatistics(Guid pinId, IReadOnlyList<double> samples)
        {
            PinId = pinId;
            Samples = samples;

            if (samples.Count == 0)
            {
                MeanPower = 0;
                StdDeviation = 0;
                return;
            }

            MeanPower = samples.Average();
            StdDeviation = CalculateStdDev(samples, MeanPower);
        }

        private static double CalculateStdDev(
            IReadOnlyList<double> values, double mean)
        {
            if (values.Count <= 1)
                return 0;

            double sumSquaredDiff = 0;
            foreach (double v in values)
            {
                double diff = v - mean;
                sumSquaredDiff += diff * diff;
            }

            return Math.Sqrt(sumSquaredDiff / (values.Count - 1));
        }
    }
}
