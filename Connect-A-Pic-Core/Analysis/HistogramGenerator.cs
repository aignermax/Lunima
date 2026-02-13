namespace CAP_Core.Analysis
{
    /// <summary>
    /// Generates histogram buckets from a collection of power samples.
    /// </summary>
    public static class HistogramGenerator
    {
        /// <summary>
        /// Default number of bins for histogram generation.
        /// </summary>
        public const int DefaultBucketCount = 10;

        /// <summary>
        /// Creates a histogram from the given samples.
        /// </summary>
        /// <param name="samples">The power samples to bin.</param>
        /// <param name="bucketCount">Number of bins to create.</param>
        /// <returns>A list of histogram buckets covering the sample range.</returns>
        public static List<HistogramBucket> Generate(
            IReadOnlyList<double> samples, int bucketCount = DefaultBucketCount)
        {
            if (bucketCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(bucketCount), "Bucket count must be positive.");

            if (samples.Count == 0)
                return new List<HistogramBucket>();

            double min = samples.Min();
            double max = samples.Max();

            if (Math.Abs(max - min) < 1e-15)
            {
                return new List<HistogramBucket>
                {
                    new(min, max, samples.Count)
                };
            }

            double binWidth = (max - min) / bucketCount;
            var counts = new int[bucketCount];

            foreach (double sample in samples)
            {
                int index = (int)((sample - min) / binWidth);
                if (index >= bucketCount)
                    index = bucketCount - 1;
                counts[index]++;
            }

            var buckets = new List<HistogramBucket>(bucketCount);
            for (int i = 0; i < bucketCount; i++)
            {
                double lower = min + i * binWidth;
                double upper = min + (i + 1) * binWidth;
                buckets.Add(new HistogramBucket(lower, upper, counts[i]));
            }

            return buckets;
        }
    }
}
