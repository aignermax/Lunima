namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// A single validation finding for an S-Matrix check.
    /// </summary>
    public class SMatrixValidationEntry
    {
        /// <summary>
        /// The severity of this validation finding.
        /// </summary>
        public SMatrixValidationSeverity Severity { get; }

        /// <summary>
        /// A human-readable description of the validation finding.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Creates a new validation entry.
        /// </summary>
        /// <param name="severity">The severity level.</param>
        /// <param name="message">Description of the finding.</param>
        public SMatrixValidationEntry(SMatrixValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        /// <inheritdoc />
        public override string ToString() => $"[{Severity}] {Message}";
    }
}
