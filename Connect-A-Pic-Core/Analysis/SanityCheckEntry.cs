namespace CAP_Core.Analysis
{
    /// <summary>
    /// A single finding from a post-simulation sanity check.
    /// </summary>
    public class SanityCheckEntry
    {
        /// <summary>
        /// The severity of this finding.
        /// </summary>
        public SanityCheckSeverity Severity { get; }

        /// <summary>
        /// The category of the check that produced this entry.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// A human-readable description of the finding.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Creates a new sanity check entry.
        /// </summary>
        /// <param name="severity">The severity level.</param>
        /// <param name="category">The check category (e.g. "Unconnected Pins").</param>
        /// <param name="message">Description of the finding.</param>
        public SanityCheckEntry(
            SanityCheckSeverity severity,
            string category,
            string message)
        {
            Severity = severity;
            Category = category;
            Message = message;
        }

        /// <inheritdoc />
        public override string ToString() => $"[{Severity}] {Category}: {Message}";
    }
}
