namespace CAP_Core.Analysis
{
    /// <summary>
    /// Aggregates multiple sanity checks and runs them against a simulation context.
    /// Produces a unified SanityCheckResult with color-coded severity.
    /// </summary>
    public class SanityChecker
    {
        private readonly IReadOnlyList<ISanityCheck> _checks;

        /// <summary>
        /// Creates a SanityChecker with the given checks.
        /// </summary>
        /// <param name="checks">The checks to run during analysis.</param>
        public SanityChecker(IEnumerable<ISanityCheck> checks)
        {
            _checks = checks.ToList();
        }

        /// <summary>
        /// Creates a SanityChecker with all default checks enabled.
        /// </summary>
        public static SanityChecker CreateDefault()
        {
            return new SanityChecker(new ISanityCheck[]
            {
                new UnconnectedPinsCheck(),
                new EnergyConservationCheck(),
                new HighLossPathCheck(),
                new NoLightFlowCheck(),
            });
        }

        /// <summary>
        /// Runs all configured checks against the given context.
        /// </summary>
        /// <param name="context">The simulation context to analyze.</param>
        /// <returns>A result containing all findings from all checks.</returns>
        public SanityCheckResult RunAll(SanityCheckContext context)
        {
            var result = new SanityCheckResult();

            foreach (var check in _checks)
            {
                var entries = check.Run(context);
                result.AddRange(entries);
            }

            return result;
        }
    }
}
