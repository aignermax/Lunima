namespace CAP_Core.Analysis
{
    /// <summary>
    /// Severity level for a sanity check finding.
    /// </summary>
    public enum SanityCheckSeverity
    {
        /// <summary>
        /// Informational note that does not indicate a problem.
        /// </summary>
        Info,

        /// <summary>
        /// A potential issue that may affect simulation accuracy.
        /// </summary>
        Warning,

        /// <summary>
        /// A critical issue that likely indicates a design problem.
        /// </summary>
        Error
    }
}
