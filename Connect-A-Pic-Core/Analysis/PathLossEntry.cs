using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Represents the loss analysis for a single input-to-output path
    /// consisting of one or more waveguide connections.
    /// </summary>
    public class PathLossEntry
    {
        /// <summary>
        /// The ordered sequence of connections forming this path.
        /// </summary>
        public IReadOnlyList<WaveguideConnection> Connections { get; }

        /// <summary>
        /// Total accumulated loss in dB across all connections in this path.
        /// </summary>
        public double TotalLossDb { get; }

        /// <summary>
        /// Loss severity classification based on total loss.
        /// </summary>
        public LossSeverity Severity { get; }

        /// <summary>
        /// Human-readable label identifying the path endpoints.
        /// </summary>
        public string PathLabel { get; }

        /// <summary>
        /// Creates a new path loss entry from a sequence of connections.
        /// </summary>
        /// <param name="connections">The ordered connections forming the path.</param>
        /// <param name="pathLabel">A descriptive label for this path.</param>
        public PathLossEntry(
            IReadOnlyList<WaveguideConnection> connections,
            string pathLabel)
        {
            Connections = connections;
            PathLabel = pathLabel;
            TotalLossDb = connections.Sum(c => c.TotalLossDb);
            Severity = LossSeverityClassifier.Classify(TotalLossDb);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{PathLabel}: {TotalLossDb:F2} dB [{Severity}]";
        }
    }
}
