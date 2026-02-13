namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Severity level for an S-Matrix validation entry.
    /// </summary>
    public enum SMatrixValidationSeverity
    {
        /// <summary>
        /// An informational warning that does not prevent simulation.
        /// </summary>
        Warning,

        /// <summary>
        /// A critical error that indicates invalid or unstable data.
        /// </summary>
        Error
    }
}
