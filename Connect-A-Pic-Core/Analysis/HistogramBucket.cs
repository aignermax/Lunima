namespace CAP_Core.Analysis
{
    /// <summary>
    /// A single bucket (bin) in a power distribution histogram.
    /// </summary>
    public class HistogramBucket
    {
        /// <summary>
        /// Lower bound of this bucket (inclusive).
        /// </summary>
        public double LowerBound { get; }

        /// <summary>
        /// Upper bound of this bucket (exclusive, except for the last bucket).
        /// </summary>
        public double UpperBound { get; }

        /// <summary>
        /// Number of samples that fall within this bucket.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Creates a new histogram bucket.
        /// </summary>
        /// <param name="lowerBound">Inclusive lower bound.</param>
        /// <param name="upperBound">Exclusive upper bound.</param>
        /// <param name="count">Number of samples in this bucket.</param>
        public HistogramBucket(double lowerBound, double upperBound, int count)
        {
            LowerBound = lowerBound;
            UpperBound = upperBound;
            Count = count;
        }
    }
}
