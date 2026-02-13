namespace CAP_Core.Analysis
{
    /// <summary>
    /// Classifies optical loss values into severity levels using standard thresholds.
    /// </summary>
    public static class LossSeverityClassifier
    {
        /// <summary>
        /// Loss threshold in dB below which severity is Low (green).
        /// </summary>
        public const double LowThresholdDb = 3.0;

        /// <summary>
        /// Loss threshold in dB below which severity is Medium (yellow).
        /// Above this threshold, severity is High (red).
        /// </summary>
        public const double MediumThresholdDb = 10.0;

        /// <summary>
        /// Classifies a loss value in dB into a severity level.
        /// </summary>
        /// <param name="lossDb">The loss value in dB (must be non-negative).</param>
        /// <returns>The severity classification for the given loss.</returns>
        public static LossSeverity Classify(double lossDb)
        {
            if (lossDb < LowThresholdDb)
                return LossSeverity.Low;

            if (lossDb < MediumThresholdDb)
                return LossSeverity.Medium;

            return LossSeverity.High;
        }
    }
}
