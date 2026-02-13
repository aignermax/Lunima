namespace CAP_Core.Analysis
{
    /// <summary>
    /// Interface for individual post-simulation sanity checks.
    /// Each implementation checks one specific aspect of the design.
    /// </summary>
    public interface ISanityCheck
    {
        /// <summary>
        /// Runs this check against the given context and returns findings.
        /// </summary>
        /// <param name="context">The simulation context to analyze.</param>
        /// <returns>Zero or more findings from this check.</returns>
        IEnumerable<SanityCheckEntry> Run(SanityCheckContext context);
    }
}
