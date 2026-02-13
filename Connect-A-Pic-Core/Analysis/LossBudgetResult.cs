using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Contains the complete loss budget analysis results for a photonic system.
    /// Provides statistics, path breakdowns, and identifies critical connections.
    /// </summary>
    public class LossBudgetResult
    {
        /// <summary>
        /// All analyzed input-to-output paths with their loss details.
        /// </summary>
        public IReadOnlyList<PathLossEntry> Paths { get; }

        /// <summary>
        /// Minimum total loss in dB across all paths. Zero if no paths exist.
        /// </summary>
        public double MinLossDb { get; }

        /// <summary>
        /// Maximum total loss in dB across all paths. Zero if no paths exist.
        /// </summary>
        public double MaxLossDb { get; }

        /// <summary>
        /// Average total loss in dB across all paths. Zero if no paths exist.
        /// </summary>
        public double AverageLossDb { get; }

        /// <summary>
        /// The path with the highest total loss, or null if no paths exist.
        /// </summary>
        public PathLossEntry? HighestLossPath { get; }

        /// <summary>
        /// Connections that appear in at least one High-severity path.
        /// These are the bottleneck connections limiting system feasibility.
        /// </summary>
        public IReadOnlyList<WaveguideConnection> CriticalConnections { get; }

        /// <summary>
        /// Creates a new loss budget result from analyzed paths.
        /// </summary>
        /// <param name="paths">All analyzed paths.</param>
        /// <param name="criticalConnections">Connections in high-severity paths.</param>
        public LossBudgetResult(
            IReadOnlyList<PathLossEntry> paths,
            IReadOnlyList<WaveguideConnection> criticalConnections)
        {
            Paths = paths;
            CriticalConnections = criticalConnections;

            if (paths.Count > 0)
            {
                MinLossDb = paths.Min(p => p.TotalLossDb);
                MaxLossDb = paths.Max(p => p.TotalLossDb);
                AverageLossDb = paths.Average(p => p.TotalLossDb);
                HighestLossPath = paths.OrderByDescending(p => p.TotalLossDb).First();
            }
        }

        /// <summary>
        /// Returns all paths matching the given severity level.
        /// </summary>
        /// <param name="severity">The severity to filter by.</param>
        /// <returns>Paths with the specified severity.</returns>
        public IEnumerable<PathLossEntry> GetPathsBySeverity(LossSeverity severity)
        {
            return Paths.Where(p => p.Severity == severity);
        }
    }
}
