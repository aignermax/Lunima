namespace CAP_Core.Analysis
{
    /// <summary>
    /// Checks for waveguide connections with excessive loss (>20 dB).
    /// High loss paths may indicate routing problems or unrealistic designs.
    /// </summary>
    public class HighLossPathCheck : ISanityCheck
    {
        private const string Category = "High Loss Path";

        /// <summary>
        /// Loss threshold in dB above which a warning is generated.
        /// </summary>
        public const double LossThresholdDb = 20.0;

        /// <inheritdoc />
        public IEnumerable<SanityCheckEntry> Run(SanityCheckContext context)
        {
            foreach (var connection in context.Connections)
            {
                if (connection.TotalLossDb > LossThresholdDb)
                {
                    var startName = connection.StartPin?.Name ?? "?";
                    var endName = connection.EndPin?.Name ?? "?";
                    var startComp =
                        connection.StartPin?.ParentComponent?.Identifier ?? "?";
                    var endComp =
                        connection.EndPin?.ParentComponent?.Identifier ?? "?";

                    yield return new SanityCheckEntry(
                        SanityCheckSeverity.Warning,
                        Category,
                        $"Connection {startComp}.{startName} -> " +
                        $"{endComp}.{endName} has {connection.TotalLossDb:F1} dB " +
                        $"loss (threshold: {LossThresholdDb:F0} dB).");
                }
            }
        }
    }
}
